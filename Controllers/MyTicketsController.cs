using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Services;
using QRCoder;

namespace DatVeXe.Controllers
{
    public class MyTicketsController : Controller
    {
        private readonly DatVeXeContext _context;
        private readonly IQRCodeService _qrCodeService;

        public MyTicketsController(DatVeXeContext context, IQRCodeService qrCodeService)
        {
            _context = context;
            _qrCodeService = qrCodeService;
        }

        // GET: MyTickets
        public async Task<IActionResult> Index(string? trangThaiLoc, DateTime? tuNgay, DateTime? denNgay, string? searchTerm)
        {
            // Kiểm tra đăng nhập
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var query = _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.Xe)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .Where(v => v.NguoiDungId == userId.Value);

            // Áp dụng bộ lọc
            if (!string.IsNullOrEmpty(trangThaiLoc))
            {
                if (Enum.TryParse<TrangThaiVe>(trangThaiLoc, out var trangThai))
                {
                    query = query.Where(v => v.VeTrangThai == trangThai);
                }
            }

            if (tuNgay.HasValue)
            {
                query = query.Where(v => v.NgayDat >= tuNgay.Value.Date);
            }

            if (denNgay.HasValue)
            {
                query = query.Where(v => v.NgayDat <= denNgay.Value.Date.AddDays(1));
            }

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(v => 
                    v.MaVe.Contains(searchTerm) ||
                    v.TenKhach.Contains(searchTerm) ||
                    v.SoDienThoai.Contains(searchTerm) ||
                    (v.ChuyenXe != null && (
                        v.ChuyenXe.DiemDi.Contains(searchTerm) ||
                        v.ChuyenXe.DiemDen.Contains(searchTerm)
                    ))
                );
            }

            var allTickets = await query.OrderByDescending(v => v.NgayDat).ToListAsync();

            var viewModel = new MyTicketsViewModel
            {
                VeDaDat = allTickets.Where(v => v.VeTrangThai == TrangThaiVe.DaDat).ToList(),
                VeSapDi = allTickets.Where(v => v.VeTrangThai == TrangThaiVe.DaDat && 
                    v.ChuyenXe != null && v.ChuyenXe.NgayKhoiHanh > DateTime.Now &&
                    v.ChuyenXe.NgayKhoiHanh <= DateTime.Now.AddDays(7)).ToList(),
                VeDaHoanThanh = allTickets.Where(v => v.VeTrangThai == TrangThaiVe.DaHoanThanh).ToList(),
                VeDaHuy = allTickets.Where(v => v.VeTrangThai == TrangThaiVe.DaHuy).ToList(),
                
                TongVe = allTickets.Count,
                TongChiTieu = allTickets.Where(v => v.VeTrangThai != TrangThaiVe.DaHuy).Sum(v => v.GiaVe),
                VeChuaDanhGia = allTickets.Count(v => v.VeTrangThai == TrangThaiVe.DaHoanThanh && 
                    !_context.DanhGiaChuyenDis.Any(d => d.VeId == v.VeId)),
                
                TrangThaiLoc = trangThaiLoc,
                TuNgay = tuNgay,
                DenNgay = denNgay,
                SearchTerm = searchTerm
            };

            return View(viewModel);
        }

        // GET: MyTickets/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.Xe)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .FirstOrDefaultAsync(v => v.VeId == id && v.NguoiDungId == userId.Value);

            if (ve == null)
            {
                return NotFound();
            }

            // Tạo QR Code sử dụng QRCodeService
            var qrCodeData = _qrCodeService.GenerateTicketQRData(ve);
            
            // Kiểm tra có thể hủy/đổi vé không
            var canCancel = ve.VeTrangThai == TrangThaiVe.DaDat && 
                           ve.ChuyenXe != null && 
                           ve.ChuyenXe.NgayKhoiHanh > DateTime.Now.AddHours(2);
            
            var canModify = ve.VeTrangThai == TrangThaiVe.DaDat && 
                           ve.ChuyenXe != null && 
                           ve.ChuyenXe.NgayKhoiHanh > DateTime.Now.AddHours(24);
            
            var canReview = ve.VeTrangThai == TrangThaiVe.DaHoanThanh && 
                           !await _context.DanhGiaChuyenDis.AnyAsync(d => d.VeId == ve.VeId);

            // Lấy đánh giá nếu có
            var danhGia = await _context.DanhGiaChuyenDis
                .FirstOrDefaultAsync(d => d.VeId == ve.VeId);

            // Lấy thông tin thanh toán
            var thanhToan = ve.ThanhToans?.FirstOrDefault();

            var viewModel = new TicketDetailViewModel
            {
                Ve = ve,
                QRCodeData = qrCodeData,
                CanCancel = canCancel,
                CanModify = canModify,
                CanReview = canReview,
                DanhGia = danhGia,
                ThanhToan = thanhToan,
                TrangThaiDisplay = GetTrangThaiDisplay(ve.TrangThai),
                DiemDonTra = $"{ve.ChuyenXe?.DiemDi} → {ve.ChuyenXe?.DiemDen}",
                ThoiGianConLai = ve.ChuyenXe != null && ve.ChuyenXe.NgayKhoiHanh > DateTime.Now ? 
                    ve.ChuyenXe.NgayKhoiHanh - DateTime.Now : TimeSpan.Zero
            };

            return View(viewModel);
        }

        // GET: MyTickets/TestQR - Test QR Code functionality
        public IActionResult TestQR()
        {
            return View();
        }

        // GET: MyTickets/QRCode/5
        public async Task<IActionResult> QRCode(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return Unauthorized();
            }

            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                .Include(v => v.ChoNgoi)
                .FirstOrDefaultAsync(v => v.VeId == id && v.NguoiDungId == userId.Value);

            if (ve == null)
            {
                return NotFound();
            }

            var qrCodeData = _qrCodeService.GenerateTicketQRData(ve);

            // Sinh QR code bằng QRCoder (không dùng System.Drawing)
            using (var qrGenerator = new QRCodeGenerator())
            using (var qrData = qrGenerator.CreateQrCode(qrCodeData, QRCodeGenerator.ECCLevel.Q))
            {
                var pngQrCode = new PngByteQRCode(qrData);
                var qrCodeBytes = pngQrCode.GetGraphic(20);
                return File(qrCodeBytes, "image/png");
            }
        }

        // POST: MyTickets/Cancel/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id, string? lyDoHuy)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                .FirstOrDefaultAsync(v => v.VeId == id && v.NguoiDungId == userId.Value);

            if (ve == null)
            {
                return NotFound();
            }

            // Kiểm tra có thể hủy không
            if (ve.VeTrangThai != TrangThaiVe.DaDat || 
                ve.ChuyenXe == null || 
                ve.ChuyenXe.NgayKhoiHanh <= DateTime.Now.AddHours(2))
            {
                TempData["ErrorMessage"] = "Không thể hủy vé này. Vé chỉ có thể hủy trước 2 giờ khởi hành.";
                return RedirectToAction("Details", new { id });
            }

            // Cập nhật trạng thái vé
            ve.TrangThai = TrangThaiVe.DaHuy;
            ve.GhiChu = $"Đã hủy lúc {DateTime.Now:dd/MM/yyyy HH:mm}. Lý do: {lyDoHuy ?? "Không có lý do"}";

            // Giải phóng ghế
            if (ve.ChoNgoiId.HasValue)
            {
                var choNgoi = await _context.ChoNgois.FindAsync(ve.ChoNgoiId.Value);
                if (choNgoi != null)
                {
                    choNgoi.TrangThaiHoatDong = true; // Giải phóng ghế
                }
            }

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đã hủy vé thành công. Tiền sẽ được hoàn lại trong 3-5 ngày làm việc.";
            return RedirectToAction("Details", new { id });
        }

        private string GetTrangThaiDisplay(TrangThaiVe trangThai)
        {
            return trangThai switch
            {
                TrangThaiVe.DaDat => "Đã đặt",
                TrangThaiVe.DaThanhToan => "Đã thanh toán",
                TrangThaiVe.DaHoanThanh => "Đã hoàn thành",
                TrangThaiVe.DaHuy => "Đã hủy",
                _ => "Không xác định"
            };
        }
    }
}
