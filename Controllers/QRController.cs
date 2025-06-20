using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Services;

namespace DatVeXe.Controllers
{
    public class QRController : Controller
    {
        private readonly DatVeXeContext _context;
        private readonly IQRCodeService _qrCodeService;
        private readonly ILogger<QRController> _logger;

        public QRController(DatVeXeContext context, IQRCodeService qrCodeService, ILogger<QRController> logger)
        {
            _context = context;
            _qrCodeService = qrCodeService;
            _logger = logger;
        }

        // GET: QR/Scanner - Trang quét QR code
        public IActionResult Scanner()
        {
            return View();
        }

        // POST: QR/Validate - Validate QR code
        [HttpPost]
        public async Task<IActionResult> Validate([FromBody] QRValidationRequest request)
        {
            try
            {
                _logger.LogInformation($"QR validation request: {request.QRData}");

                // Validate QR data format
                if (!_qrCodeService.ValidateTicketQR(request.QRData, out var ticketData, out string errorMessage))
                {
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = errorMessage
                    });
                }

                // Find ticket in database
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .FirstOrDefaultAsync(v => v.MaVe == ticketData.TicketCode);

                if (ve == null)
                {
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = "Không tìm thấy vé trong hệ thống"
                    });
                }

                // Validate ticket status
                if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                {
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = "Vé đã bị hủy",
                        TicketInfo = CreateTicketInfo(ve)
                    });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaSuDung)
                {
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = "Vé đã được sử dụng",
                        TicketInfo = CreateTicketInfo(ve)
                    });
                }

                // Check payment status
                var hasValidPayment = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) ?? false;
                if (!hasValidPayment && ve.VeTrangThai != TrangThaiVe.DaThanhToan)
                {
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = "Vé chưa được thanh toán",
                        TicketInfo = CreateTicketInfo(ve)
                    });
                }

                // Check departure time
                if (ve.ChuyenXe != null)
                {
                    var now = DateTime.Now;
                    var departureTime = ve.ChuyenXe.NgayKhoiHanh;
                    
                    // Allow check-in 2 hours before departure
                    if (now < departureTime.AddHours(-2))
                    {
                        return Json(new QRValidationResponse
                        {
                            Success = false,
                            Message = $"Chưa đến thời gian check-in. Vui lòng quay lại sau {departureTime.AddHours(-2):dd/MM/yyyy HH:mm}",
                            TicketInfo = CreateTicketInfo(ve)
                        });
                    }

                    // Don't allow check-in after departure + 1 hour
                    if (now > departureTime.AddHours(1))
                    {
                        return Json(new QRValidationResponse
                        {
                            Success = false,
                            Message = "Vé đã quá hạn sử dụng",
                            TicketInfo = CreateTicketInfo(ve)
                        });
                    }
                }

                // Validate passenger info matches QR data
                if (!string.Equals(ve.TenKhach.Trim(), ticketData.PassengerName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning($"Passenger name mismatch: DB={ve.TenKhach}, QR={ticketData.PassengerName}");
                    return Json(new QRValidationResponse
                    {
                        Success = false,
                        Message = "Thông tin hành khách không khớp",
                        TicketInfo = CreateTicketInfo(ve)
                    });
                }

                // All validations passed
                return Json(new QRValidationResponse
                {
                    Success = true,
                    Message = "Vé hợp lệ",
                    TicketInfo = CreateTicketInfo(ve),
                    CanCheckIn = ve.VeTrangThai == TrangThaiVe.DaThanhToan || ve.VeTrangThai == TrangThaiVe.DaDat
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating QR code");
                return Json(new QRValidationResponse
                {
                    Success = false,
                    Message = "Lỗi hệ thống khi xử lý QR code"
                });
            }
        }

        // POST: QR/CheckIn - Check in passenger
        [HttpPost]
        public async Task<IActionResult> CheckIn([FromBody] CheckInRequest request)
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .FirstOrDefaultAsync(v => v.MaVe == request.TicketCode);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaSuDung)
                {
                    return Json(new { success = false, message = "Vé đã được check-in trước đó" });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                {
                    return Json(new { success = false, message = "Vé đã bị hủy" });
                }

                // Update ticket status to used
                ve.VeTrangThai = TrangThaiVe.DaSuDung;
                ve.NgayCapNhat = DateTime.Now;
                
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Passenger checked in: {ve.MaVe} - {ve.TenKhach}");

                return Json(new { 
                    success = true, 
                    message = $"Check-in thành công cho hành khách {ve.TenKhach}",
                    ticketCode = ve.MaVe,
                    passengerName = ve.TenKhach,
                    seatNumber = ve.ChoNgoi?.SoGhe
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during check-in");
                return Json(new { success = false, message = "Lỗi hệ thống khi check-in" });
            }
        }

        private TicketInfo CreateTicketInfo(Ve ve)
        {
            return new TicketInfo
            {
                TicketCode = ve.MaVe,
                PassengerName = ve.TenKhach,
                Phone = ve.SoDienThoai,
                SeatNumber = ve.ChoNgoi?.SoGhe ?? "",
                Route = ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDiDisplay} - {ve.ChuyenXe.DiemDenDisplay}" : "",
                DepartureTime = ve.ChuyenXe?.NgayKhoiHanh ?? DateTime.MinValue,
                Amount = ve.GiaVe,
                Status = ve.VeTrangThai.ToString(),
                PaymentStatus = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true ? "Đã thanh toán" : "Chưa thanh toán"
            };
        }
    }

    public class QRValidationRequest
    {
        public string QRData { get; set; } = string.Empty;
    }

    public class QRValidationResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TicketInfo? TicketInfo { get; set; }
        public bool CanCheckIn { get; set; }
    }

    public class CheckInRequest
    {
        public string TicketCode { get; set; } = string.Empty;
    }

    public class TicketInfo
    {
        public string TicketCode { get; set; } = string.Empty;
        public string PassengerName { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;
        public string SeatNumber { get; set; } = string.Empty;
        public string Route { get; set; } = string.Empty;
        public DateTime DepartureTime { get; set; }
        public decimal Amount { get; set; }
        public string Status { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = string.Empty;
    }
}
