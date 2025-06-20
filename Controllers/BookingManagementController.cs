using DatVeXe.Models;
using DatVeXe.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DatVeXe.Controllers
{
    public class BookingManagementController : Controller
    {
        private readonly DatVeXeContext _context;

        public BookingManagementController(DatVeXeContext context)
        {
            _context = context;
        }

        // Kiểm tra đăng nhập và quyền admin
        private bool IsLoggedIn()
        {
            return HttpContext.Session.GetInt32("UserId").HasValue;
        }

        private bool IsAdmin()
        {
            return HttpContext.Session.GetInt32("IsAdmin") == 1;
        }

        // GET: BookingManagement - Danh sách chuyến xe để quản lý với bộ lọc nâng cao
        public async Task<IActionResult> Index(DateTime? ngayKhoiHanh, string? trangThai, string? searchKeyword,
            DateTime? tuNgay, DateTime? denNgay, string? loaiXe, decimal? minGia, decimal? maxGia, string? tinhTrangGhe)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            var query = _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .Include(c => c.Ves)
                .AsQueryable();

            // Lọc theo ngày khởi hành cụ thể
            if (ngayKhoiHanh.HasValue)
            {
                query = query.Where(c => c.NgayKhoiHanh.Date == ngayKhoiHanh.Value.Date);
            }
            // Lọc theo khoảng thời gian
            else if (tuNgay.HasValue || denNgay.HasValue)
            {
                if (tuNgay.HasValue)
                {
                    query = query.Where(c => c.NgayKhoiHanh.Date >= tuNgay.Value.Date);
                }
                if (denNgay.HasValue)
                {
                    query = query.Where(c => c.NgayKhoiHanh.Date <= denNgay.Value.Date);
                }
            }
            else
            {
                // Mặc định hiển thị chuyến xe trong 7 ngày tới
                var fromDate = DateTime.Today;
                var toDate = DateTime.Today.AddDays(7);
                query = query.Where(c => c.NgayKhoiHanh.Date >= fromDate && c.NgayKhoiHanh.Date <= toDate);
            }

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(trangThai) && trangThai != "all")
            {
                switch (trangThai)
                {
                    case "chua_khoi_hanh":
                        query = query.Where(c => c.NgayKhoiHanh > DateTime.Now);
                        break;
                    case "da_khoi_hanh":
                        query = query.Where(c => c.NgayKhoiHanh <= DateTime.Now);
                        break;
                }
            }

            // Tìm kiếm theo từ khóa
            if (!string.IsNullOrEmpty(searchKeyword))
            {
                var keyword = searchKeyword.ToLower().Trim();
                query = query.Where(c =>
                    (c.TuyenDuong != null && (c.TuyenDuong.DiemDi.ToLower().Contains(keyword) ||
                                             c.TuyenDuong.DiemDen.ToLower().Contains(keyword))) ||
                    (c.Xe != null && c.Xe.BienSoXe.ToLower().Contains(keyword)) ||
                    (c.TaiXe != null && c.TaiXe.HoTen.ToLower().Contains(keyword))
                );
            }

            // Lọc theo loại xe
            if (!string.IsNullOrEmpty(loaiXe))
            {
                query = query.Where(c => c.Xe != null && c.Xe.LoaiXe == loaiXe);
            }

            // Lọc theo khoảng giá
            if (minGia.HasValue)
            {
                query = query.Where(c => c.Gia >= minGia.Value);
            }
            if (maxGia.HasValue)
            {
                query = query.Where(c => c.Gia <= maxGia.Value);
            }

            var chuyenXes = await query.ToListAsync();

            // Lọc theo tình trạng ghế (phải làm sau khi load data vì cần tính toán)
            if (!string.IsNullOrEmpty(tinhTrangGhe))
            {
                switch (tinhTrangGhe)
                {
                    case "con_trong":
                        chuyenXes = chuyenXes.Where(c =>
                        {
                            var totalSeats = c.Xe?.SoGhe ?? 0;
                            var bookedSeats = c.Ves?.Count ?? 0;
                            return bookedSeats < totalSeats;
                        }).ToList();
                        break;
                    case "gan_het":
                        chuyenXes = chuyenXes.Where(c =>
                        {
                            var totalSeats = c.Xe?.SoGhe ?? 0;
                            var bookedSeats = c.Ves?.Count ?? 0;
                            var percentBooked = totalSeats > 0 ? (double)bookedSeats / totalSeats * 100 : 0;
                            return percentBooked > 80 && percentBooked < 100;
                        }).ToList();
                        break;
                    case "het_cho":
                        chuyenXes = chuyenXes.Where(c =>
                        {
                            var totalSeats = c.Xe?.SoGhe ?? 0;
                            var bookedSeats = c.Ves?.Count ?? 0;
                            return bookedSeats >= totalSeats;
                        }).ToList();
                        break;
                }
            }

            // Sắp xếp kết quả
            chuyenXes = chuyenXes.OrderBy(c => c.NgayKhoiHanh).ToList();

            // Truyền dữ liệu cho ViewBag
            ViewBag.NgayKhoiHanh = ngayKhoiHanh;
            ViewBag.TrangThai = trangThai ?? "all";
            ViewBag.SearchKeyword = searchKeyword;
            ViewBag.TuNgay = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgay = denNgay?.ToString("yyyy-MM-dd");
            ViewBag.LoaiXe = loaiXe;
            ViewBag.MinGia = minGia;
            ViewBag.MaxGia = maxGia;
            ViewBag.TinhTrangGhe = tinhTrangGhe;

            return View(chuyenXes);
        }

        // GET: BookingManagement/Reports - Trang báo cáo chuyên dụng
        public async Task<IActionResult> Reports()
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);
            var startOfYear = new DateTime(now.Year, 1, 1);

            // Thống kê tổng quan
            var totalTrips = await _context.ChuyenXes.CountAsync();
            var totalTickets = await _context.Ves.CountAsync();
            var totalRevenue = await _context.ThanhToans
                .Where(t => t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Thống kê theo tháng hiện tại
            var monthlyTrips = await _context.ChuyenXes
                .Where(c => c.NgayKhoiHanh >= startOfMonth && c.NgayKhoiHanh <= endOfMonth)
                .CountAsync();
            var monthlyTickets = await _context.Ves
                .Where(v => v.NgayDat >= startOfMonth && v.NgayDat <= endOfMonth)
                .CountAsync();
            var monthlyRevenue = await _context.ThanhToans
                .Where(t => t.NgayThanhToan >= startOfMonth && t.NgayThanhToan <= endOfMonth &&
                           t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Top tuyến đường theo doanh thu
            var topRoutesByRevenue = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Where(v => v.ChuyenXe.TuyenDuong != null)
                .GroupBy(v => new {
                    v.ChuyenXe.TuyenDuong.DiemDi,
                    v.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    Route = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    Revenue = g.Sum(v => v.GiaVe),
                    TicketCount = g.Count(),
                    TripCount = g.Select(v => v.ChuyenXeId).Distinct().Count()
                })
                .OrderByDescending(r => r.Revenue)
                .Take(10)
                .ToListAsync();

            // Top tuyến đường theo số vé
            var topRoutesByTickets = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Where(v => v.ChuyenXe.TuyenDuong != null)
                .GroupBy(v => new {
                    v.ChuyenXe.TuyenDuong.DiemDi,
                    v.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    Route = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    TicketCount = g.Count(),
                    Revenue = g.Sum(v => v.GiaVe),
                    AvgPrice = g.Average(v => v.GiaVe)
                })
                .OrderByDescending(r => r.TicketCount)
                .Take(10)
                .ToListAsync();

            // Thống kê theo trạng thái vé
            var ticketStatusStats = await _context.Ves
                .GroupBy(v => v.VeTrangThai)
                .Select(g => new {
                    Status = g.Key,
                    Count = g.Count(),
                    Revenue = g.Sum(v => v.GiaVe)
                })
                .ToListAsync();

            // Thống kê doanh thu theo ngày trong tháng
            var dailyRevenueThisMonth = await _context.ThanhToans
                .Where(t => t.NgayThanhToan >= startOfMonth && t.NgayThanhToan <= endOfMonth &&
                           t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .GroupBy(t => t.NgayThanhToan.Date)
                .Select(g => new {
                    Date = g.Key,
                    Revenue = g.Sum(t => t.SoTien),
                    Count = g.Count()
                })
                .OrderBy(d => d.Date)
                .ToListAsync();

            // Thống kê theo loại xe
            var vehicleTypeStats = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.Ves)
                .Where(c => c.Xe != null)
                .GroupBy(c => c.Xe.LoaiXe)
                .Select(g => new {
                    VehicleType = g.Key,
                    TripCount = g.Count(),
                    TicketCount = g.Sum(c => c.Ves.Count),
                    Revenue = g.Sum(c => c.Ves.Sum(v => v.GiaVe)),
                    AvgOccupancy = g.Average(c => c.Xe.SoGhe > 0 ? (double)c.Ves.Count / c.Xe.SoGhe * 100 : 0)
                })
                .ToListAsync();

            ViewBag.TotalTrips = totalTrips;
            ViewBag.TotalTickets = totalTickets;
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.MonthlyTrips = monthlyTrips;
            ViewBag.MonthlyTickets = monthlyTickets;
            ViewBag.MonthlyRevenue = monthlyRevenue;
            ViewBag.TopRoutesByRevenue = topRoutesByRevenue;
            ViewBag.TopRoutesByTickets = topRoutesByTickets;
            ViewBag.TicketStatusStats = ticketStatusStats;
            ViewBag.DailyRevenueThisMonth = dailyRevenueThisMonth;
            ViewBag.VehicleTypeStats = vehicleTypeStats;
            ViewBag.CurrentMonth = startOfMonth.ToString("MM/yyyy");

            return View();
        }

        // POST: BookingManagement/TransferTicket - Chuyển vé sang chuyến xe khác
        [HttpPost]
        public async Task<IActionResult> TransferTicket(int veId, int newChuyenXeId, string? reason)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền thực hiện thao tác này" });
            }

            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Include(v => v.ChoNgoi)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                var newChuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.Ves)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == newChuyenXeId);

                if (newChuyenXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy chuyến xe đích" });
                }

                // Kiểm tra chuyến xe mới còn chỗ trống
                var availableSeats = (newChuyenXe.Xe?.SoGhe ?? 0) - (newChuyenXe.Ves?.Count ?? 0);
                if (availableSeats <= 0)
                {
                    return Json(new { success = false, message = "Chuyến xe đích đã hết chỗ" });
                }

                // Kiểm tra vé có thể chuyển (chưa sử dụng, chưa hủy)
                if (ve.TrangThai == TrangThaiVe.DaSuDung || ve.TrangThai == TrangThaiVe.DaHuy || ve.TrangThai == TrangThaiVe.DaHoanThanh)
                {
                    return Json(new { success = false, message = "Không thể chuyển vé đã sử dụng, đã hủy hoặc đã hoàn thành" });
                }

                // Lưu thông tin cũ để log
                var oldChuyenXeId = ve.ChuyenXeId;
                var oldChoNgoiId = ve.ChoNgoiId;

                // Cập nhật vé
                ve.ChuyenXeId = newChuyenXeId;
                ve.ChoNgoiId = null; // Reset chỗ ngồi, sẽ chọn lại
                ve.GiaVe = newChuyenXe.Gia; // Cập nhật giá vé mới
                ve.GhiChu = $"Chuyển từ chuyến {oldChuyenXeId}. Lý do: {reason ?? "Không có"}";

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = "Đã chuyển vé thành công",
                    newTripInfo = $"{newChuyenXe.DiemDiDisplay} → {newChuyenXe.DiemDenDisplay} - {newChuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: BookingManagement/DuplicateTicket - Tạo vé trùng lặp
        [HttpPost]
        public async Task<IActionResult> DuplicateTicket(int veId, int quantity = 1)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền thực hiện thao tác này" });
            }

            try
            {
                var originalVe = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c.Xe)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (originalVe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé gốc" });
                }

                // Kiểm tra chuyến xe còn đủ chỗ
                var currentTickets = await _context.Ves.CountAsync(v => v.ChuyenXeId == originalVe.ChuyenXeId);
                var totalSeats = originalVe.ChuyenXe?.Xe?.SoGhe ?? 0;
                var availableSeats = totalSeats - currentTickets;

                if (availableSeats < quantity)
                {
                    return Json(new { success = false, message = $"Chuyến xe chỉ còn {availableSeats} chỗ trống, không thể tạo {quantity} vé" });
                }

                var newTickets = new List<Ve>();
                for (int i = 0; i < quantity; i++)
                {
                    var newVe = new Ve
                    {
                        ChuyenXeId = originalVe.ChuyenXeId,
                        TenKhach = originalVe.TenKhach + $" (Bản sao {i + 1})",
                        SoDienThoai = originalVe.SoDienThoai,
                        Email = originalVe.Email,
                        GiaVe = originalVe.GiaVe,
                        NgayDat = DateTime.Now,
                        TrangThai = TrangThaiVe.DaDat,
                        MaVe = GenerateTicketCode(),
                        GhiChu = $"Sao chép từ vé {originalVe.MaVe}"
                    };
                    newTickets.Add(newVe);
                }

                _context.Ves.AddRange(newTickets);
                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = $"Đã tạo {quantity} vé sao chép thành công",
                    newTicketCodes = newTickets.Select(v => v.MaVe).ToList()
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: BookingManagement/MergeTickets - Gộp nhiều vé thành nhóm
        [HttpPost]
        public async Task<IActionResult> MergeTickets(List<int> veIds, string groupName)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền thực hiện thao tác này" });
            }

            try
            {
                if (veIds == null || veIds.Count < 2)
                {
                    return Json(new { success = false, message = "Cần chọn ít nhất 2 vé để gộp nhóm" });
                }

                var ves = await _context.Ves
                    .Where(v => veIds.Contains(v.VeId))
                    .Include(v => v.ChuyenXe)
                    .ToListAsync();

                if (ves.Count != veIds.Count)
                {
                    return Json(new { success = false, message = "Một số vé không tồn tại" });
                }

                // Kiểm tra tất cả vé cùng chuyến xe
                var chuyenXeIds = ves.Select(v => v.ChuyenXeId).Distinct().ToList();
                if (chuyenXeIds.Count > 1)
                {
                    return Json(new { success = false, message = "Chỉ có thể gộp các vé cùng chuyến xe" });
                }

                // Cập nhật ghi chú cho tất cả vé trong nhóm
                var groupNote = $"Nhóm: {groupName} ({ves.Count} vé)";
                foreach (var ve in ves)
                {
                    ve.GhiChu = string.IsNullOrEmpty(ve.GhiChu) ? groupNote : $"{ve.GhiChu}. {groupNote}";
                }

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = $"Đã gộp {ves.Count} vé thành nhóm '{groupName}' thành công"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: BookingManagement/FindDuplicateTickets - Tìm vé trùng lặp
        public async Task<IActionResult> FindDuplicateTickets(int chuyenXeId)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền thực hiện thao tác này" });
            }

            try
            {
                var duplicates = await _context.Ves
                    .Where(v => v.ChuyenXeId == chuyenXeId)
                    .GroupBy(v => new { v.TenKhach, v.SoDienThoai })
                    .Where(g => g.Count() > 1)
                    .Select(g => new {
                        TenKhach = g.Key.TenKhach,
                        SoDienThoai = g.Key.SoDienThoai,
                        SoVe = g.Count(),
                        VeIds = g.Select(v => v.VeId).ToList(),
                        MaVes = g.Select(v => v.MaVe).ToList()
                    })
                    .ToListAsync();

                return Json(new { success = true, duplicates = duplicates });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Hàm tạo mã vé
        private string GenerateTicketCode()
        {
            var random = new Random();
            var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
            var randomNumber = random.Next(1000, 9999);
            return $"VE{timestamp}{randomNumber}";
        }

        // GET: BookingManagement/Details/5 - Chi tiết booking của chuyến xe
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .Include(c => c.Ves)
                    .ThenInclude(v => v.ChoNgoi)
                .Include(c => c.Ves)
                    .ThenInclude(v => v.ThanhToans)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction(nameof(Index));
            }

            return View(chuyenXe);
        }

        // POST: BookingManagement/UpdateTicketStatus - Cập nhật trạng thái vé
        [HttpPost]
        public async Task<IActionResult> UpdateTicketStatus(int veId, TrangThaiVe trangThai, string? ghiChu)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền truy cập" });
            }

            try
            {
                var ve = await _context.Ves.FindAsync(veId);
                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                var oldStatus = ve.VeTrangThai;
                ve.VeTrangThai = trangThai;
                
                if (!string.IsNullOrEmpty(ghiChu))
                {
                    ve.GhiChu = ghiChu;
                }

                // Cập nhật thời gian tương ứng
                switch (trangThai)
                {
                    case TrangThaiVe.DaHuy:
                        ve.NgayHuy = DateTime.Now;
                        ve.LyDoHuy = ghiChu;
                        break;
                    case TrangThaiVe.DaSuDung:
                        // Đánh dấu đã sử dụng (đã đón khách)
                        break;
                    case TrangThaiVe.DaHoanThanh:
                        // Đánh dấu hoàn thành chuyến đi
                        break;
                }

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = $"Đã cập nhật trạng thái vé từ '{StatusHelper.GetStatusText(oldStatus)}' thành '{StatusHelper.GetStatusText(trangThai)}'"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: BookingManagement/UpdateMultipleTickets - Cập nhật nhiều vé cùng lúc
        [HttpPost]
        public async Task<IActionResult> UpdateMultipleTickets(List<int> veIds, TrangThaiVe trangThai, string? ghiChu)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền truy cập" });
            }

            try
            {
                var ves = await _context.Ves.Where(v => veIds.Contains(v.VeId)).ToListAsync();
                
                if (!ves.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy vé nào" });
                }

                foreach (var ve in ves)
                {
                    ve.VeTrangThai = trangThai;

                    if (!string.IsNullOrEmpty(ghiChu))
                    {
                        ve.GhiChu = ghiChu;
                    }

                    // Cập nhật thời gian tương ứng
                    switch (trangThai)
                    {
                    case TrangThaiVe.DaHuy:
                        ve.NgayHuy = DateTime.Now;
                        ve.LyDoHuy = ghiChu;
                        break;
                    case TrangThaiVe.DaSuDung:
                        // Đánh dấu đã sử dụng (đã đón khách)
                        break;
                    case TrangThaiVe.DaHoanThanh:
                        // Đánh dấu hoàn thành chuyến đi
                        break;
                    }
                }

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = $"Đã cập nhật {ves.Count} vé thành trạng thái '{StatusHelper.GetStatusText(trangThai)}'"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }
        
        // POST: BookingManagement/SendNotification - Gửi thông báo cho hành khách
        [HttpPost]
        public async Task<IActionResult> SendNotification(List<int> veIds, string subject, string message)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Không có quyền truy cập" });
            }
            
            try
            {
                if (string.IsNullOrEmpty(subject) || string.IsNullOrEmpty(message))
                {
                    return Json(new { success = false, message = "Vui lòng nhập tiêu đề và nội dung thông báo" });
                }
                
                var ves = await _context.Ves
                    .Where(v => veIds.Contains(v.VeId))
                    .ToListAsync();
                
                if (!ves.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy vé nào" });
                }
                
                // Đây là nơi thực hiện gửi email (giả lập thành công cho mục đích demo)
                
                return Json(new { 
                    success = true, 
                    message = $"Đã gửi thông báo đến {ves.Count} hành khách thành công" 
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: BookingManagement/ExportReport - Xuất báo cáo với bộ lọc
        public async Task<IActionResult> ExportReport(DateTime? ngayKhoiHanh, string? trangThai, string? searchKeyword,
            DateTime? tuNgay, DateTime? denNgay, string? loaiXe, decimal? minGia, decimal? maxGia,
            string? tinhTrangGhe, string reportType = "detailed")
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            try
            {
                // Sử dụng cùng logic lọc như trong Index
                var query = _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.TuyenDuong)
                    .Include(c => c.TaiXe)
                    .Include(c => c.Ves)
                        .ThenInclude(v => v.ChoNgoi)
                    .Include(c => c.Ves)
                        .ThenInclude(v => v.ThanhToans)
                    .AsQueryable();

                // Áp dụng các bộ lọc (copy từ Index action)
                if (ngayKhoiHanh.HasValue)
                {
                    query = query.Where(c => c.NgayKhoiHanh.Date == ngayKhoiHanh.Value.Date);
                }
                else if (tuNgay.HasValue || denNgay.HasValue)
                {
                    if (tuNgay.HasValue)
                        query = query.Where(c => c.NgayKhoiHanh.Date >= tuNgay.Value.Date);
                    if (denNgay.HasValue)
                        query = query.Where(c => c.NgayKhoiHanh.Date <= denNgay.Value.Date);
                }
                else
                {
                    var fromDate = DateTime.Today;
                    var toDate = DateTime.Today.AddDays(7);
                    query = query.Where(c => c.NgayKhoiHanh.Date >= fromDate && c.NgayKhoiHanh.Date <= toDate);
                }

                if (!string.IsNullOrEmpty(trangThai) && trangThai != "all")
                {
                    switch (trangThai)
                    {
                        case "chua_khoi_hanh":
                            query = query.Where(c => c.NgayKhoiHanh > DateTime.Now);
                            break;
                        case "da_khoi_hanh":
                            query = query.Where(c => c.NgayKhoiHanh <= DateTime.Now);
                            break;
                    }
                }

                if (!string.IsNullOrEmpty(searchKeyword))
                {
                    var keyword = searchKeyword.ToLower().Trim();
                    query = query.Where(c =>
                        (c.TuyenDuong != null && (c.TuyenDuong.DiemDi.ToLower().Contains(keyword) ||
                                                 c.TuyenDuong.DiemDen.ToLower().Contains(keyword))) ||
                        (c.Xe != null && c.Xe.BienSoXe.ToLower().Contains(keyword)) ||
                        (c.TaiXe != null && c.TaiXe.HoTen.ToLower().Contains(keyword))
                    );
                }

                if (!string.IsNullOrEmpty(loaiXe))
                    query = query.Where(c => c.Xe != null && c.Xe.LoaiXe == loaiXe);

                if (minGia.HasValue)
                    query = query.Where(c => c.Gia >= minGia.Value);
                if (maxGia.HasValue)
                    query = query.Where(c => c.Gia <= maxGia.Value);

                var chuyenXes = await query.OrderBy(c => c.NgayKhoiHanh).ToListAsync();

                // Lọc theo tình trạng ghế
                if (!string.IsNullOrEmpty(tinhTrangGhe))
                {
                    switch (tinhTrangGhe)
                    {
                        case "con_trong":
                            chuyenXes = chuyenXes.Where(c =>
                            {
                                var totalSeats = c.Xe?.SoGhe ?? 0;
                                var bookedSeats = c.Ves?.Count ?? 0;
                                return bookedSeats < totalSeats;
                            }).ToList();
                            break;
                        case "gan_het":
                            chuyenXes = chuyenXes.Where(c =>
                            {
                                var totalSeats = c.Xe?.SoGhe ?? 0;
                                var bookedSeats = c.Ves?.Count ?? 0;
                                var percentBooked = totalSeats > 0 ? (double)bookedSeats / totalSeats * 100 : 0;
                                return percentBooked > 80 && percentBooked < 100;
                            }).ToList();
                            break;
                        case "het_cho":
                            chuyenXes = chuyenXes.Where(c =>
                            {
                                var totalSeats = c.Xe?.SoGhe ?? 0;
                                var bookedSeats = c.Ves?.Count ?? 0;
                                return bookedSeats >= totalSeats;
                            }).ToList();
                            break;
                    }
                }

                // Tạo báo cáo theo loại
                switch (reportType.ToLower())
                {
                    case "summary":
                        return ExportSummaryReport(chuyenXes);
                    case "revenue":
                        return ExportRevenueReport(chuyenXes);
                    case "csv":
                        return ExportCsvReport(chuyenXes);
                    default:
                        return ExportDetailedReport(chuyenXes);
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Có lỗi xảy ra khi xuất báo cáo: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // GET: BookingManagement/ExportPassengerList/5 - Xuất danh sách hành khách
        public async Task<IActionResult> ExportPassengerList(int id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .Include(c => c.Ves)
                    .ThenInclude(v => v.ChoNgoi)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction(nameof(Index));
            }

            // Tạo CSV content
            var csv = new StringBuilder();
            csv.AppendLine("STT,Tên khách hàng,Số điện thoại,Email,Số ghế,Trạng thái,Ngày đặt,Ghi chú");

            var stt = 1;
            foreach (var ve in chuyenXe.Ves.OrderBy(v => v.ChoNgoi?.SoGhe))
            {
                csv.AppendLine($"{stt},{ve.TenKhach},{ve.SoDienThoai},{ve.Email},{ve.ChoNgoi?.SoGhe},{StatusHelper.GetStatusText(ve.VeTrangThai)},{ve.NgayDat:dd/MM/yyyy HH:mm},{ve.GhiChu}");
                stt++;
            }

            var fileName = $"DanhSachHanhKhach_{chuyenXe.DiemDiDisplay}_{chuyenXe.DiemDenDisplay}_{chuyenXe.NgayKhoiHanh:yyyyMMdd_HHmm}.csv";
            var bytes = Encoding.UTF8.GetBytes(csv.ToString());

            return File(bytes, "text/csv", fileName);
        }



        // Xuất báo cáo tổng hợp
        private IActionResult ExportSummaryReport(List<ChuyenXe> chuyenXes)
        {
            var csv = new StringBuilder();

            // Tiêu đề
            csv.AppendLine("BÁO CÁO TỔNG HỢP QUẢN LÝ ĐƠN ĐẶT VÉ");
            csv.AppendLine($"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
            csv.AppendLine($"Tổng số chuyến xe: {chuyenXes.Count}");
            csv.AppendLine();

            // Thống kê tổng quan
            var totalTickets = chuyenXes.Sum(c => c.Ves?.Count ?? 0);
            var totalRevenue = chuyenXes.Sum(c => c.Ves?.Sum(v => v.GiaVe) ?? 0);
            var totalSeats = chuyenXes.Sum(c => c.Xe?.SoGhe ?? 0);
            var bookedSeats = chuyenXes.Sum(c => c.Ves?.Count ?? 0);

            csv.AppendLine("THỐNG KÊ TỔNG QUAN");
            csv.AppendLine($"Tổng số vé đã bán: {totalTickets}");
            csv.AppendLine($"Tổng doanh thu: {totalRevenue:N0} VNĐ");
            csv.AppendLine($"Tổng số ghế: {totalSeats}");
            csv.AppendLine($"Số ghế đã đặt: {bookedSeats}");
            csv.AppendLine($"Tỷ lệ lấp đầy: {(totalSeats > 0 ? (double)bookedSeats / totalSeats * 100 : 0):F2}%");
            csv.AppendLine();

            // Chi tiết theo chuyến xe
            csv.AppendLine("CHI TIẾT THEO CHUYẾN XE");
            csv.AppendLine("Tuyến đường,Ngày khởi hành,Xe,Tổng ghế,Đã đặt,Tỷ lệ (%),Doanh thu (VNĐ),Trạng thái");

            foreach (var chuyenXe in chuyenXes)
            {
                var seats = chuyenXe.Xe?.SoGhe ?? 0;
                var booked = chuyenXe.Ves?.Count ?? 0;
                var revenue = chuyenXe.Ves?.Sum(v => v.GiaVe) ?? 0;
                var percentage = seats > 0 ? (double)booked / seats * 100 : 0;
                var status = chuyenXe.NgayKhoiHanh > DateTime.Now ? "Sắp khởi hành" : "Đã khởi hành";

                csv.AppendLine($"\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\"," +
                              $"{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm}," +
                              $"{chuyenXe.Xe?.BienSoXe ?? ""}," +
                              $"{seats}," +
                              $"{booked}," +
                              $"{percentage:F2}," +
                              $"{revenue:N0}," +
                              $"{status}");
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"BaoCaoTongHop_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // Xuất báo cáo doanh thu
        private IActionResult ExportRevenueReport(List<ChuyenXe> chuyenXes)
        {
            var csv = new StringBuilder();

            // Tiêu đề
            csv.AppendLine("BÁO CÁO DOANH THU THEO CHUYẾN XE");
            csv.AppendLine($"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
            csv.AppendLine();

            // Headers
            csv.AppendLine("Tuyến đường,Ngày khởi hành,Xe,Giá vé (VNĐ),Số vé bán,Doanh thu (VNĐ),Đã thanh toán (VNĐ),Chưa thanh toán (VNĐ),Đã hủy (VNĐ),Hoàn tiền (VNĐ)");

            decimal totalRevenue = 0;
            decimal totalPaid = 0;
            decimal totalUnpaid = 0;
            decimal totalRefunded = 0;

            foreach (var chuyenXe in chuyenXes)
            {
                var ves = chuyenXe.Ves ?? new List<Ve>();
                var revenue = ves.Sum(v => v.GiaVe);
                var paidAmount = ves.Where(v => v.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true).Sum(v => v.GiaVe);
                var unpaidAmount = ves.Where(v => v.TrangThai != TrangThaiVe.DaHuy && !(v.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true)).Sum(v => v.GiaVe);
                var cancelledAmount = ves.Where(v => v.TrangThai == TrangThaiVe.DaHuy).Sum(v => v.GiaVe);
                var refundedAmount = ves.Where(v => v.TrangThai == TrangThaiVe.DaHoanTien).Sum(v => v.GiaVe);

                csv.AppendLine($"\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\"," +
                              $"{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm}," +
                              $"{chuyenXe.Xe?.BienSoXe ?? ""}," +
                              $"{chuyenXe.Gia:N0}," +
                              $"{ves.Count}," +
                              $"{revenue:N0}," +
                              $"{paidAmount:N0}," +
                              $"{unpaidAmount:N0}," +
                              $"{cancelledAmount:N0}," +
                              $"{refundedAmount:N0}");

                totalRevenue += revenue;
                totalPaid += paidAmount;
                totalUnpaid += unpaidAmount;
                totalRefunded += refundedAmount;
            }

            // Tổng cộng
            csv.AppendLine();
            csv.AppendLine($"TỔNG CỘNG,,,,,{totalRevenue:N0},{totalPaid:N0},{totalUnpaid:N0},,{totalRefunded:N0}");

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"BaoCaoDoanhThu_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // Xuất báo cáo chi tiết
        private IActionResult ExportDetailedReport(List<ChuyenXe> chuyenXes)
        {
            var csv = new StringBuilder();

            // Tiêu đề
            csv.AppendLine("BÁO CÁO CHI TIẾT QUẢN LÝ ĐƠN ĐẶT VÉ");
            csv.AppendLine($"Thời gian xuất: {DateTime.Now:dd/MM/yyyy HH:mm}");
            csv.AppendLine();

            // Headers
            csv.AppendLine("Mã vé,Tuyến đường,Ngày khởi hành,Xe,Ghế,Tên khách,Số điện thoại,Email,Ngày đặt,Giá vé (VNĐ),Trạng thái,Ghi chú");

            foreach (var chuyenXe in chuyenXes)
            {
                var ves = chuyenXe.Ves ?? new List<Ve>();
                if (!ves.Any())
                {
                    csv.AppendLine($"Chưa có vé,\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\",{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm},{chuyenXe.Xe?.BienSoXe ?? ""},,,,,,Chưa có đặt vé,");
                }
                else
                {
                    foreach (var ve in ves.OrderBy(v => v.ChoNgoi?.SoGhe))
                    {
                        csv.AppendLine($"\"{ve.MaVe}\",\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\",{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm},{chuyenXe.Xe?.BienSoXe ?? ""},{ve.ChoNgoi?.SoGhe ?? ""},\"{ve.TenKhach}\",{ve.SoDienThoai},\"{ve.Email ?? ""}\",{ve.NgayDat:dd/MM/yyyy HH:mm},{ve.GiaVe:N0},\"{StatusHelper.GetStatusText(ve.VeTrangThai)}\",\"{ve.GhiChu ?? ""}\"");
                    }
                }
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"BaoCaoChiTiet_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }

        // Xuất báo cáo CSV
        private IActionResult ExportCsvReport(List<ChuyenXe> chuyenXes)
        {
            var csv = new StringBuilder();

            // Headers
            csv.AppendLine("Mã vé,Tuyến đường,Ngày khởi hành,Xe,Ghế,Tên khách,Số điện thoại,Email,Ngày đặt,Giá vé,Trạng thái,Ghi chú");

            foreach (var chuyenXe in chuyenXes)
            {
                var ves = chuyenXe.Ves ?? new List<Ve>();
                if (!ves.Any())
                {
                    csv.AppendLine($"Chưa có vé,\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\",{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm},{chuyenXe.Xe?.BienSoXe ?? ""},,,,,,Chưa có đặt vé,");
                }
                else
                {
                    foreach (var ve in ves.OrderBy(v => v.ChoNgoi?.SoGhe))
                    {
                        csv.AppendLine($"\"{ve.MaVe}\",\"{chuyenXe.DiemDiDisplay} → {chuyenXe.DiemDenDisplay}\",{chuyenXe.NgayKhoiHanh:dd/MM/yyyy HH:mm},{chuyenXe.Xe?.BienSoXe ?? ""},{ve.ChoNgoi?.SoGhe ?? ""},\"{ve.TenKhach}\",{ve.SoDienThoai},\"{ve.Email ?? ""}\",{ve.NgayDat:dd/MM/yyyy HH:mm},{ve.GiaVe:N0},\"{StatusHelper.GetStatusText(ve.VeTrangThai)}\",\"{ve.GhiChu ?? ""}\"");
                    }
                }
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            var fileName = $"BaoCaoChiTiet_{DateTime.Now:yyyyMMdd_HHmmss}.csv";

            return File(bytes, "text/csv; charset=utf-8", fileName);
        }
    }
}
