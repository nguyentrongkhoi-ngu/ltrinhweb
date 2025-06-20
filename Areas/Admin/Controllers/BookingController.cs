using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Services;
using DatVeXe.Attributes;
using DatVeXe.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class BookingController : Controller
    {
        private readonly DatVeXeContext _context;
        private readonly IEmailService _emailService;
        private readonly ISMSService _smsService;
        private readonly IPaymentService _paymentService;
        private readonly ITripSearchService _tripSearchService;
        private readonly ILogger<BookingController> _logger;

        public BookingController(DatVeXeContext context, IEmailService emailService, ISMSService smsService,
            IPaymentService paymentService, ITripSearchService tripSearchService, ILogger<BookingController> logger)
        {
            _context = context;
            _emailService = emailService;
            _smsService = smsService;
            _paymentService = paymentService;
            _tripSearchService = tripSearchService;
            _logger = logger;
        }

        // GET: Admin/Booking/List
        public async Task<IActionResult> List(string searchString, int? chuyenXeId, string trangThaiVe,
            DateTime? tuNgay, DateTime? denNgay, int page = 1)
        {
            try
            {
                // Validate input parameters
                if (page < 1) page = 1;
                if (tuNgay.HasValue && denNgay.HasValue && tuNgay > denNgay)
                {
                    TempData["Warning"] = "Ngày bắt đầu không thể lớn hơn ngày kết thúc";
                    tuNgay = null;
                    denNgay = null;
                }

                ViewBag.CurrentFilter = searchString?.Trim();
                ViewBag.SelectedChuyenXeId = chuyenXeId;
                ViewBag.SelectedTrangThaiVe = trangThaiVe;
                ViewBag.TuNgayFilter = tuNgay?.ToString("yyyy-MM-dd");
                ViewBag.DenNgayFilter = denNgay?.ToString("yyyy-MM-dd");

                var ves = _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.Xe)
                    .Include(v => v.NguoiDung)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .AsQueryable();

                // Tìm kiếm với null-safe operations
                if (!string.IsNullOrWhiteSpace(searchString))
                {
                    var searchTerm = searchString.Trim().ToLower();
                    ves = ves.Where(v =>
                        (v.NguoiDung != null && v.NguoiDung.HoTen != null && v.NguoiDung.HoTen.ToLower().Contains(searchTerm)) ||
                        (v.NguoiDung != null && v.NguoiDung.Email != null && v.NguoiDung.Email.ToLower().Contains(searchTerm)) ||
                        (v.NguoiDung != null && v.NguoiDung.SoDienThoai != null && v.NguoiDung.SoDienThoai.Contains(searchTerm)) ||
                        (v.TenKhach != null && v.TenKhach.ToLower().Contains(searchTerm)) ||
                        (v.SoDienThoai != null && v.SoDienThoai.Contains(searchTerm)) ||
                        (v.MaVe != null && v.MaVe.ToLower().Contains(searchTerm)));
                }

                // Lọc theo chuyến xe
                if (chuyenXeId.HasValue && chuyenXeId.Value > 0)
                {
                    ves = ves.Where(v => v.ChuyenXeId == chuyenXeId.Value);
                }

                // Lọc theo trạng thái vé
                if (!string.IsNullOrEmpty(trangThaiVe))
                {
                    if (Enum.TryParse<TrangThaiVe>(trangThaiVe, out var trangThai))
                    {
                        ves = ves.Where(v => v.VeTrangThai == trangThai);
                    }
                }

                // Lọc theo ngày
                if (tuNgay.HasValue)
                {
                    ves = ves.Where(v => v.NgayDat.Date >= tuNgay.Value.Date);
                }
                if (denNgay.HasValue)
                {
                    ves = ves.Where(v => v.NgayDat.Date <= denNgay.Value.Date);
                }

                // Phân trang
                int pageSize = 15;
                var totalCount = await ves.CountAsync();
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

                // Validate page number
                if (page > totalPages && totalPages > 0) page = totalPages;

                ViewBag.CurrentPage = page;
                ViewBag.TotalPages = totalPages;
                ViewBag.TotalCount = totalCount;
                ViewBag.HasPrevious = page > 1;
                ViewBag.HasNext = page < totalPages;

                var pagedVes = await ves
                    .OrderByDescending(v => v.NgayDat)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Load dropdown data efficiently
                await LoadDropdownDataAsync();

                return View(pagedVes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while loading booking list");
                TempData["Error"] = "Có lỗi xảy ra khi tải danh sách đặt vé. Vui lòng thử lại.";
                return View(new List<Ve>());
            }
        }

        private async Task LoadDropdownDataAsync()
        {
            try
            {
                // Danh sách chuyến xe cho dropdown - chỉ lấy những chuyến có vé
                ViewBag.ChuyenXeList = await _context.ChuyenXes
                    .Include(c => c.TuyenDuong)
                    .Where(c => c.Ves.Any()) // Chỉ lấy chuyến xe có vé
                    .Select(c => new {
                        c.ChuyenXeId,
                        TenChuyenXe = c.TuyenDuong != null
                            ? $"{c.TuyenDuong.DiemDi} → {c.TuyenDuong.DiemDen} ({c.NgayKhoiHanh:dd/MM/yyyy HH:mm})"
                            : $"Chuyến xe {c.ChuyenXeId} ({c.NgayKhoiHanh:dd/MM/yyyy HH:mm})"
                    })
                    .OrderByDescending(c => c.ChuyenXeId)
                    .Take(100) // Giới hạn số lượng để tránh dropdown quá dài
                    .ToListAsync();

                // Danh sách trạng thái vé
                ViewBag.TrangThaiVeList = Enum.GetValues<TrangThaiVe>()
                    .Select(t => new { Value = t.ToString(), Text = StatusHelper.GetStatusText(t) })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error loading dropdown data");
                ViewBag.ChuyenXeList = new List<object>();
                ViewBag.TrangThaiVeList = new List<object>();
            }
        }

        // GET: Admin/Booking/Detail/5
        public async Task<IActionResult> Detail(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.Xe)
                .Include(v => v.NguoiDung)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .FirstOrDefaultAsync(v => v.VeId == id);

            if (ve == null)
            {
                return NotFound();
            }

            return View(ve);
        }

        // GET: Admin/Booking/PrintTicket/5
        public async Task<IActionResult> PrintTicket(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.Xe)
                .Include(v => v.NguoiDung)
                .Include(v => v.ChoNgoi)
                .FirstOrDefaultAsync(v => v.VeId == id);

            if (ve == null)
            {
                return NotFound();
            }

            return View(ve);
        }

        // POST: Admin/Booking/UpdateStatus
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<JsonResult> UpdateStatus(int veId, string trangThai, string? ghiChu)
        {
            try
            {
                if (veId <= 0)
                {
                    return Json(new { success = false, message = "ID vé không hợp lệ" });
                }

                if (string.IsNullOrWhiteSpace(trangThai))
                {
                    return Json(new { success = false, message = "Trạng thái không được để trống" });
                }

                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Include(v => v.NguoiDung)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                if (!Enum.TryParse<TrangThaiVe>(trangThai, out var newStatus))
                {
                    return Json(new { success = false, message = "Trạng thái không hợp lệ" });
                }

                // Validate business rules
                var validationResult = StatusHelper.ValidateStatusChange(ve.VeTrangThai, newStatus);
                if (!validationResult.IsValid)
                {
                    return Json(new { success = false, message = validationResult.ErrorMessage });
                }

                // Update status and related fields
                var oldStatus = ve.VeTrangThai;
                ve.VeTrangThai = newStatus;

                if (!string.IsNullOrWhiteSpace(ghiChu))
                {
                    ve.GhiChu = ghiChu.Trim();
                }

                // Set specific fields based on status
                switch (newStatus)
                {
                    case TrangThaiVe.DaHuy:
                        ve.NgayHuy = DateTime.Now;
                        ve.LyDoHuy = ghiChu?.Trim() ?? "Hủy bởi admin";
                        break;
                    case TrangThaiVe.DaSuDung:
                        // Mark as used (passenger boarded)
                        break;
                    case TrangThaiVe.DaHoanThanh:
                        // Mark as completed
                        break;
                }

                await _context.SaveChangesAsync();

                // Log the status change
                _logger.LogInformation($"Ticket {ve.MaVe} status changed from {oldStatus} to {newStatus} by admin");

                // TODO: Send notification to customer if needed
                // await NotifyCustomerStatusChange(ve, oldStatus, newStatus);

                return Json(new {
                    success = true,
                    message = $"Cập nhật trạng thái vé thành công từ '{StatusHelper.GetStatusText(oldStatus)}' sang '{StatusHelper.GetStatusText(newStatus)}'",
                    newStatus = StatusHelper.GetStatusText(newStatus),
                    newStatusValue = newStatus.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ticket status for VeId: {veId}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi cập nhật trạng thái vé. Vui lòng thử lại." });
            }
        }



        // GET: Admin/Booking/Statistics
        public async Task<IActionResult> Statistics()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Thống kê tổng quan
            var tongVe = await _context.Ves.CountAsync();
            var veDaThanhToan = await _context.Ves.CountAsync(v => v.VeTrangThai == TrangThaiVe.DaThanhToan);
            var veDaHuy = await _context.Ves.CountAsync(v => v.VeTrangThai == TrangThaiVe.DaHuy);
            var veThangNay = await _context.Ves
                .Where(v => v.NgayDat >= startOfMonth && v.NgayDat <= endOfMonth)
                .CountAsync();

            // Doanh thu
            var doanhThuThangNay = await _context.ThanhToans
                .Where(t => t.NgayThanhToan >= startOfMonth &&
                           t.NgayThanhToan <= endOfMonth &&
                           t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Top tuyến đường có nhiều vé nhất
            var topTuyenDuong = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Where(v => v.ChuyenXe.TuyenDuong != null)
                .GroupBy(v => new {
                    v.ChuyenXe.TuyenDuong.DiemDi,
                    v.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    SoVe = g.Count(),
                    DoanhThu = g.Sum(v => v.GiaVe)
                })
                .OrderByDescending(x => x.SoVe)
                .Take(5)
                .ToListAsync();

            ViewBag.TongVe = tongVe;
            ViewBag.VeDaThanhToan = veDaThanhToan;
            ViewBag.VeDaHuy = veDaHuy;
            ViewBag.VeThangNay = veThangNay;
            ViewBag.DoanhThuThangNay = doanhThuThangNay;
            ViewBag.TopTuyenDuong = topTuyenDuong;

            return View();
        }

        // GET: Admin/Booking/Export
        public async Task<IActionResult> Export(string searchString, int? chuyenXeId, string trangThaiVe,
            DateTime? tuNgay, DateTime? denNgay, string format = "csv")
        {
            try
            {
                // Validate date range
                if (tuNgay.HasValue && denNgay.HasValue && tuNgay > denNgay)
                {
                    TempData["Error"] = "Ngày bắt đầu không thể lớn hơn ngày kết thúc";
                    return RedirectToAction("List");
                }

                var ves = _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.Xe)
                    .Include(v => v.NguoiDung)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .AsQueryable();

                // Apply same filters as List action with null-safe operations
                if (!string.IsNullOrWhiteSpace(searchString))
                {
                    var searchTerm = searchString.Trim().ToLower();
                    ves = ves.Where(v =>
                        (v.NguoiDung != null && v.NguoiDung.HoTen != null && v.NguoiDung.HoTen.ToLower().Contains(searchTerm)) ||
                        (v.NguoiDung != null && v.NguoiDung.Email != null && v.NguoiDung.Email.ToLower().Contains(searchTerm)) ||
                        (v.NguoiDung != null && v.NguoiDung.SoDienThoai != null && v.NguoiDung.SoDienThoai.Contains(searchTerm)) ||
                        (v.TenKhach != null && v.TenKhach.ToLower().Contains(searchTerm)) ||
                        (v.SoDienThoai != null && v.SoDienThoai.Contains(searchTerm)) ||
                        (v.MaVe != null && v.MaVe.ToLower().Contains(searchTerm)));
                }

                if (chuyenXeId.HasValue && chuyenXeId.Value > 0)
                {
                    ves = ves.Where(v => v.ChuyenXeId == chuyenXeId.Value);
                }

                if (!string.IsNullOrEmpty(trangThaiVe))
                {
                    if (Enum.TryParse<TrangThaiVe>(trangThaiVe, out var trangThai))
                    {
                        ves = ves.Where(v => v.VeTrangThai == trangThai);
                    }
                }

                if (tuNgay.HasValue)
                {
                    ves = ves.Where(v => v.NgayDat.Date >= tuNgay.Value.Date);
                }
                if (denNgay.HasValue)
                {
                    ves = ves.Where(v => v.NgayDat.Date <= denNgay.Value.Date);
                }

                var veList = await ves.OrderByDescending(v => v.NgayDat).ToListAsync();

                if (!veList.Any())
                {
                    TempData["Warning"] = "Không có dữ liệu để xuất";
                    return RedirectToAction("List");
                }

                // Generate filename with filters info
                var fileName = GenerateExportFileName(searchString, chuyenXeId, trangThaiVe, tuNgay, denNgay, format);

                if (format.ToLower() == "excel")
                {
                    return await ExportToExcel(veList, fileName);
                }
                else
                {
                    return ExportToCsv(veList, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while exporting booking data");
                TempData["Error"] = "Có lỗi xảy ra khi xuất dữ liệu. Vui lòng thử lại.";
                return RedirectToAction("List");
            }
        }

        private IActionResult ExportToCsv(List<Ve> veList, string fileName)
        {
            var csv = new StringBuilder();

            // Add BOM for UTF-8
            csv.Append('\ufeff');

            // Header with more detailed information
            csv.AppendLine("Mã vé,Họ tên khách hàng,Email,Số điện thoại,Tuyến đường,Ngày khởi hành,Giờ khởi hành,Biển số xe,Ghế,Giá vé,Trạng thái thanh toán,Trạng thái vé,Ngày đặt,Ghi chú");

            foreach (var ve in veList)
            {
                var tenKhach = !string.IsNullOrEmpty(ve.TenKhach) ? ve.TenKhach : ve.NguoiDung?.HoTen ?? "";
                var soDienThoai = !string.IsNullOrEmpty(ve.SoDienThoai) ? ve.SoDienThoai : ve.NguoiDung?.SoDienThoai ?? "";
                var email = ve.Email ?? ve.NguoiDung?.Email ?? "";

                var tuyenDuong = ve.ChuyenXe?.TuyenDuong != null
                    ? $"{ve.ChuyenXe.TuyenDuong.DiemDi} → {ve.ChuyenXe.TuyenDuong.DiemDen}"
                    : "N/A";

                var ngayKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy") ?? "";
                var gioKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("HH:mm") ?? "";
                var bienSoXe = ve.ChuyenXe?.Xe?.BienSoXe ?? "";
                var soGhe = ve.ChoNgoi?.SoGhe ?? "";

                var trangThaiThanhToan = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true
                    ? "Đã thanh toán" : "Chưa thanh toán";

                csv.AppendLine($"\"{ve.MaVe}\"," +
                              $"\"{tenKhach}\"," +
                              $"\"{email}\"," +
                              $"\"{soDienThoai}\"," +
                              $"\"{tuyenDuong}\"," +
                              $"\"{ngayKhoiHanh}\"," +
                              $"\"{gioKhoiHanh}\"," +
                              $"\"{bienSoXe}\"," +
                              $"\"{soGhe}\"," +
                              $"\"{ve.GiaVe:N0}\"," +
                              $"\"{trangThaiThanhToan}\"," +
                              $"\"{StatusHelper.GetStatusText(ve.VeTrangThai)}\"," +
                              $"\"{ve.NgayDat:dd/MM/yyyy HH:mm}\"," +
                              $"\"{ve.GhiChu ?? ""}\"");
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        private async Task<IActionResult> ExportToExcel(List<Ve> veList, string fileName)
        {
            // TODO: Implement Excel export using EPPlus or similar library
            // For now, return CSV with Excel extension
            return ExportToCsv(veList, fileName.Replace(".xlsx", ".csv"));
        }

        private string GenerateExportFileName(string searchString, int? chuyenXeId, string trangThaiVe,
            DateTime? tuNgay, DateTime? denNgay, string format)
        {
            var fileName = "DanhSachVe";

            if (!string.IsNullOrEmpty(searchString))
            {
                fileName += $"_TimKiem";
            }

            if (chuyenXeId.HasValue)
            {
                fileName += $"_ChuyenXe{chuyenXeId}";
            }

            if (!string.IsNullOrEmpty(trangThaiVe))
            {
                fileName += $"_{trangThaiVe}";
            }

            if (tuNgay.HasValue || denNgay.HasValue)
            {
                var fromDate = tuNgay?.ToString("yyyyMMdd") ?? "All";
                var toDate = denNgay?.ToString("yyyyMMdd") ?? "All";
                fileName += $"_{fromDate}_{toDate}";
            }

            fileName += $"_{DateTime.Now:yyyyMMdd_HHmmss}";
            fileName += format.ToLower() == "excel" ? ".xlsx" : ".csv";

            return fileName;
        }


    }
}
