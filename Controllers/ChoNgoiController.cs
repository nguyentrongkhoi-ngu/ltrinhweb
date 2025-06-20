using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Services;

namespace DatVeXe.Controllers
{
    public class ChoNgoiController : Controller
    {
        private readonly DatVeXeContext _context;
        private readonly IEmailService _emailService;

        public ChoNgoiController(DatVeXeContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        // GET: ChoNgoi/SoDoGhe/5 (Simple seat map)
        public async Task<IActionResult> SoDoGhe(int id)
        {
            var xe = await _context.Xes
                .FirstOrDefaultAsync(x => x.XeId == id);

            if (xe == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction("Search", "ChuyenXe");
            }

            var choNgois = await _context.ChoNgois
                .Where(c => c.XeId == id && c.TrangThaiHoatDong)
                .OrderBy(c => c.Hang)
                .ThenBy(c => c.Cot)
                .ToListAsync();

            var viewModel = new SoDoGheViewModel
            {
                XeId = xe.XeId,
                BienSo = xe.BienSo,
                LoaiXe = xe.LoaiXe,
                SoGhe = xe.SoGhe,
                SoHang = choNgois.Any() ? choNgois.Max(c => c.Hang) : 0,
                SoCot = choNgois.Any() ? choNgois.Max(c => c.Cot) : 0,
                DanhSachGhe = choNgois.Select(c => new ChoNgoiViewModel
                {
                    ChoNgoiId = c.ChoNgoiId,
                    SoGhe = c.SoGhe,
                    Hang = c.Hang,
                    Cot = c.Cot,
                    LoaiGhe = c.LoaiGhe,
                    TrangThaiHoatDong = c.TrangThaiHoatDong,
                    DaDat = false, // Sẽ cập nhật sau khi có chuyến xe cụ thể
                    TenKhachDat = null
                }).ToList()
            };

            return View(viewModel);
        }

        // GET: ChoNgoi/ChonGhe/5
        public async Task<IActionResult> ChonGhe(int id)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .ThenInclude(x => x.ChoNgois)
                .Include(c => c.TuyenDuong)
                .Include(c => c.Ves)
                .ThenInclude(v => v.ChoNgoi)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction("Search", "ChuyenXe");
            }

            if (chuyenXe.NgayKhoiHanh <= DateTime.Now)
            {
                TempData["Error"] = "Chuyến xe đã khởi hành";
                return RedirectToAction("Search", "ChuyenXe");
            }

            // Lấy danh sách chỗ ngồi của xe
            var choNgois = await _context.ChoNgois
                .Where(c => c.XeId == chuyenXe.XeId && c.TrangThaiHoatDong)
                .OrderBy(c => c.Hang)
                .ThenBy(c => c.Cot)
                .ToListAsync();

            // Lấy danh sách vé đã đặt cho chuyến xe này (bao gồm tất cả trạng thái đã sử dụng ghế)
            var veDaDat = chuyenXe.Ves?.Where(v => v.VeTrangThai == TrangThaiVe.DaDat ||
                                                   v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                                   v.VeTrangThai == TrangThaiVe.DaSuDung).ToList() ?? new List<Ve>();

            var viewModel = new ChonChoNgoiViewModel
            {
                ChuyenXe = chuyenXe,
                DanhSachChoNgoi = choNgois.Select(c => new ChoNgoiViewModel
                {
                    ChoNgoiId = c.ChoNgoiId,
                    SoGhe = c.SoGhe,
                    Hang = c.Hang,
                    Cot = c.Cot,
                    LoaiGhe = c.LoaiGhe,
                    TrangThaiHoatDong = c.TrangThaiHoatDong,
                    DaDat = veDaDat.Any(v => v.ChoNgoiId == c.ChoNgoiId),
                    TenKhachDat = veDaDat.FirstOrDefault(v => v.ChoNgoiId == c.ChoNgoiId)?.TenKhach
                }).ToList()
            };

            return View(viewModel);
        }

        // POST: ChoNgoi/DatVe
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DatVe(DatVeVoiChoNgoiViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Thông tin đặt vé không hợp lệ";
                return RedirectToAction("ChonGhe", new { id = model.ChuyenXeId });
            }

            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.Ves)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == model.ChuyenXeId);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction("Search", "ChuyenXe");
            }

            if (chuyenXe.NgayKhoiHanh <= DateTime.Now)
            {
                TempData["Error"] = "Chuyến xe đã khởi hành";
                return RedirectToAction("Search", "ChuyenXe");
            }

            // Kiểm tra chỗ ngồi đã được đặt chưa
            if (model.ChoNgoiId.HasValue)
            {
                var choNgoiDaDat = await _context.Ves
                    .AnyAsync(v => v.ChuyenXeId == model.ChuyenXeId &&
                                   v.ChoNgoiId == model.ChoNgoiId &&
                                   (v.VeTrangThai == TrangThaiVe.DaDat ||
                                    v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                    v.VeTrangThai == TrangThaiVe.DaSuDung));

                if (choNgoiDaDat)
                {
                    TempData["Error"] = "Chỗ ngồi đã được đặt bởi khách hàng khác";
                    return RedirectToAction("ChonGhe", new { id = model.ChuyenXeId });
                }
            }

            // Kiểm tra xe còn chỗ không (nếu không chọn chỗ cụ thể)
            if (!model.ChoNgoiId.HasValue)
            {
                var soVeDaDat = chuyenXe.Ves?.Count(v => v.VeTrangThai == TrangThaiVe.DaDat ||
                                                         v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                                         v.VeTrangThai == TrangThaiVe.DaSuDung) ?? 0;
                if (soVeDaDat >= chuyenXe.Xe!.SoGhe)
                {
                    TempData["Error"] = "Chuyến xe đã hết chỗ";
                    return RedirectToAction("ChonGhe", new { id = model.ChuyenXeId });
                }
            }

            try
            {
                // Tạo vé mới
                var ve = new Ve
                {
                    ChuyenXeId = model.ChuyenXeId,
                    ChoNgoiId = model.ChoNgoiId,
                    TenKhach = model.TenKhach,
                    SoDienThoai = model.SoDienThoai,
                    Email = model.Email,
                    GhiChu = model.GhiChu,
                    NgayDat = DateTime.Now,
                    GiaVe = chuyenXe.Gia,
                    VeTrangThai = TrangThaiVe.DaDat,
                    MaVe = GenerateTicketCode()
                };

                // Set user ID if logged in
                int? userId = HttpContext.Session.GetInt32("UserId");
                if (userId.HasValue)
                {
                    ve.NguoiDungId = userId.Value;
                }

                _context.Ves.Add(ve);
                await _context.SaveChangesAsync();

                // Gửi email xác nhận
                if (!string.IsNullOrEmpty(ve.Email))
                {
                    try
                    {
                        var tripInfo = $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}";
                        var seatInfo = model.ChoNgoiId.HasValue ? 
                            (await _context.ChoNgois.FindAsync(model.ChoNgoiId.Value))?.SoGhe ?? "Chưa chọn chỗ cụ thể" : 
                            "Chưa chọn chỗ cụ thể";

                        await _emailService.SendTicketConfirmationAsync(
                            ve.Email,
                            ve.TenKhach,
                            ve.MaVe,
                            tripInfo,
                            seatInfo,
                            ve.GiaVe,
                            chuyenXe.NgayKhoiHanh
                        );

                        TempData["Success"] = "Đặt vé thành công! Email xác nhận đã được gửi.";
                    }
                    catch (Exception)
                    {
                        TempData["Success"] = "Đặt vé thành công! Tuy nhiên không thể gửi email xác nhận.";
                    }
                }
                else
                {
                    TempData["Success"] = "Đặt vé thành công!";
                }

                return RedirectToAction("ThanhCong", new { id = ve.VeId });
            }
            catch (Exception)
            {
                TempData["Error"] = "Có lỗi xảy ra khi đặt vé. Vui lòng thử lại.";
                return RedirectToAction("ChonGhe", new { id = model.ChuyenXeId });
            }
        }

        // API: Lấy sơ đồ ghế
        [HttpGet]
        public async Task<IActionResult> GetSoDoGhe(int chuyenXeId)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.Ves)
                .ThenInclude(v => v.ChoNgoi)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == chuyenXeId);

            if (chuyenXe == null)
            {
                return NotFound();
            }

            var choNgois = await _context.ChoNgois
                .Where(c => c.XeId == chuyenXe.XeId && c.TrangThaiHoatDong)
                .OrderBy(c => c.Hang)
                .ThenBy(c => c.Cot)
                .ToListAsync();

            var veDaDat = chuyenXe.Ves?.Where(v => v.VeTrangThai == TrangThaiVe.DaDat ||
                                                   v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                                   v.VeTrangThai == TrangThaiVe.DaSuDung).ToList() ?? new List<Ve>();

            // Lấy thông tin reservation hiện tại
            var activeReservations = await _context.SeatReservations
                .Where(r => r.ChuyenXeId == chuyenXeId &&
                           r.IsActive &&
                           r.ExpiresAt > DateTime.Now)
                .ToListAsync();

            var sessionId = HttpContext.Session.Id;
            var userEmail = HttpContext.Session.GetString("UserEmail");

            var soDoGhe = new SoDoGheViewModel
            {
                XeId = chuyenXe.XeId,
                BienSo = chuyenXe.Xe!.BienSo,
                LoaiXe = chuyenXe.Xe.LoaiXe,
                SoGhe = chuyenXe.Xe.SoGhe,
                SoHang = choNgois.Any() ? choNgois.Max(c => c.Hang) : 0,
                SoCot = choNgois.Any() ? choNgois.Max(c => c.Cot) : 0,
                DanhSachGhe = choNgois.Select(c => {
                    var reservation = activeReservations.FirstOrDefault(r => r.ChoNgoiId == c.ChoNgoiId);
                    var isReservedByCurrentUser = reservation != null &&
                        (reservation.SessionId == sessionId ||
                         (!string.IsNullOrEmpty(userEmail) && reservation.UserEmail == userEmail));

                    return new ChoNgoiViewModel
                    {
                        ChoNgoiId = c.ChoNgoiId,
                        SoGhe = c.SoGhe,
                        Hang = c.Hang,
                        Cot = c.Cot,
                        LoaiGhe = c.LoaiGhe,
                        TrangThaiHoatDong = c.TrangThaiHoatDong,
                        DaDat = veDaDat.Any(v => v.ChoNgoiId == c.ChoNgoiId),
                        DangGiu = reservation != null && !isReservedByCurrentUser,
                        ThoiGianGiu = reservation?.ExpiresAt,
                        TenKhachDat = veDaDat.FirstOrDefault(v => v.ChoNgoiId == c.ChoNgoiId)?.TenKhach
                    };
                }).ToList()
            };

            return Json(soDoGhe);
        }

        // API: Giữ chỗ tạm thời
        [HttpPost]
        public async Task<IActionResult> GiuCho(int choNgoiId, int chuyenXeId)
        {
            try
            {
                // Kiểm tra chỗ ngồi có tồn tại và chưa được đặt
                var choNgoiDaDat = await _context.Ves
                    .AnyAsync(v => v.ChuyenXeId == chuyenXeId &&
                                   v.ChoNgoiId == choNgoiId &&
                                   (v.VeTrangThai == TrangThaiVe.DaDat ||
                                    v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                    v.VeTrangThai == TrangThaiVe.DaSuDung));

                if (choNgoiDaDat)
                {
                    return Json(new { success = false, message = "Chỗ ngồi đã được đặt" });
                }

                // Kiểm tra chỗ ngồi có đang được giữ bởi người khác không
                var existingReservation = await _context.SeatReservations
                    .FirstOrDefaultAsync(r => r.ChuyenXeId == chuyenXeId &&
                                             r.ChoNgoiId == choNgoiId &&
                                             r.IsActive &&
                                             r.ExpiresAt > DateTime.Now);

                var sessionId = HttpContext.Session.Id;
                var userEmail = HttpContext.Session.GetString("UserEmail");

                if (existingReservation != null)
                {
                    // Nếu là cùng session/user thì gia hạn
                    if (existingReservation.SessionId == sessionId ||
                        (!string.IsNullOrEmpty(userEmail) && existingReservation.UserEmail == userEmail))
                    {
                        existingReservation.ExpiresAt = DateTime.Now.AddMinutes(5);
                        await _context.SaveChangesAsync();
                        return Json(new { success = true, message = "Đã gia hạn giữ chỗ", timeout = 300 });
                    }
                    else
                    {
                        return Json(new { success = false, message = "Chỗ ngồi đang được giữ bởi người khác" });
                    }
                }

                // Tạo reservation mới
                var reservation = new SeatReservation
                {
                    ChuyenXeId = chuyenXeId,
                    ChoNgoiId = choNgoiId,
                    SessionId = sessionId,
                    UserEmail = userEmail,
                    ReservedAt = DateTime.Now,
                    ExpiresAt = DateTime.Now.AddMinutes(5),
                    IsActive = true
                };

                _context.SeatReservations.Add(reservation);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đã giữ chỗ thành công", timeout = 300 }); // 5 phút
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // API: Hủy giữ chỗ
        [HttpPost]
        public async Task<IActionResult> HuyGiuCho(int choNgoiId, int chuyenXeId)
        {
            try
            {
                var sessionId = HttpContext.Session.Id;
                var userEmail = HttpContext.Session.GetString("UserEmail");

                // Tìm reservation của user hiện tại
                var reservation = await _context.SeatReservations
                    .FirstOrDefaultAsync(r => r.ChuyenXeId == chuyenXeId &&
                                             r.ChoNgoiId == choNgoiId &&
                                             r.IsActive &&
                                             (r.SessionId == sessionId ||
                                              (!string.IsNullOrEmpty(userEmail) && r.UserEmail == userEmail)));

                if (reservation != null)
                {
                    reservation.IsActive = false;
                    await _context.SaveChangesAsync();
                    return Json(new { success = true, message = "Đã hủy giữ chỗ" });
                }

                return Json(new { success = false, message = "Không tìm thấy reservation để hủy" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // API: Dọn dẹp reservation hết hạn
        [HttpPost]
        public async Task<IActionResult> CleanupExpiredReservations()
        {
            try
            {
                var expiredReservations = await _context.SeatReservations
                    .Where(r => r.IsActive && r.ExpiresAt <= DateTime.Now)
                    .ToListAsync();

                foreach (var reservation in expiredReservations)
                {
                    reservation.IsActive = false;
                }

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = $"Đã dọn dẹp {expiredReservations.Count} reservation hết hạn",
                    count = expiredReservations.Count
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // API: Lấy thống kê reservation
        [HttpGet]
        public async Task<IActionResult> GetReservationStats()
        {
            try
            {
                var activeReservations = await _context.SeatReservations
                    .Where(r => r.IsActive && r.ExpiresAt > DateTime.Now)
                    .CountAsync();

                var expiredReservations = await _context.SeatReservations
                    .Where(r => r.IsActive && r.ExpiresAt <= DateTime.Now)
                    .CountAsync();

                var totalReservations = await _context.SeatReservations.CountAsync();

                return Json(new {
                    success = true,
                    activeReservations,
                    expiredReservations,
                    totalReservations
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: ChoNgoi/ThanhCong/5
        public async Task<IActionResult> ThanhCong(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .FirstOrDefaultAsync(v => v.VeId == id);

            if (ve == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin vé";
                return RedirectToAction("Search", "ChuyenXe");
            }

            return View(ve);
        }

        private string GenerateTicketCode()
        {
            var dateStr = DateTime.Now.ToString("yyyyMMdd");
            var random = new Random();
            var randomNum = random.Next(1000, 9999);
            return $"VE{dateStr}{randomNum}";
        }
    }
}
