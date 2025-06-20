using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Attributes;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/[controller]/[action]")]
    [AdminAuthorization]
    public class AdminController : Controller
    {
        private readonly DatVeXeContext _context;

        public AdminController(DatVeXeContext context)
        {
            _context = context;
        }

        // Dashboard chính của Admin
        public async Task<IActionResult> Index()
        {
            // Thống kê tổng quan
            var tongNguoiDung = await _context.NguoiDungs.CountAsync();
            var tongVeDaBan = await _context.Ves.CountAsync();
            var tongTuyenDuong = await _context.TuyenDuongs.CountAsync();
            var tongXe = await _context.Xes.CountAsync();
            var tongChuyenXe = await _context.ChuyenXes.CountAsync();
            var chuyenXeChoDuyet = await _context.ChuyenXes
                .CountAsync(c => c.TrangThaiDuyet == TrangThaiDuyet.ChoDuyet);

            // Doanh thu trong tháng này
            var dauThang = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var doanhThuThang = await _context.ThanhToans
                .Where(t => t.NgayThanhToan >= dauThang && t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Doanh thu hôm nay
            var homNay = DateTime.Now.Date;
            var doanhThuHomNay = await _context.ThanhToans
                .Where(t => t.NgayThanhToan.Date == homNay && t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Top 5 tuyến đường bán chạy nhất
            var topTuyenDuong = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Where(v => v.ChuyenXe.TuyenDuong != null)
                .GroupBy(v => new { 
                    v.ChuyenXe.TuyenDuong.TuyenDuongId,
                    v.ChuyenXe.TuyenDuong.DiemDi,
                    v.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    SoVe = g.Count()
                })
                .OrderByDescending(x => x.SoVe)
                .Take(5)
                .ToListAsync();

            // Đánh giá mới nhất
            var danhGiaMoiNhat = await _context.DanhGiaChuyenDis
                .Include(d => d.NguoiDung)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(d => !d.BiAn)
                .OrderByDescending(d => d.ThoiGianDanhGia)
                .Take(5)
                .ToListAsync();

            ViewBag.TongNguoiDung = tongNguoiDung;
            ViewBag.TongVeDaBan = tongVeDaBan;
            ViewBag.TongTuyenDuong = tongTuyenDuong;
            ViewBag.TongXe = tongXe;
            ViewBag.TongChuyenXe = tongChuyenXe;
            ViewBag.ChuyenXeChoDuyet = chuyenXeChoDuyet;
            ViewBag.DoanhThuThang = doanhThuThang;
            ViewBag.DoanhThuHomNay = doanhThuHomNay;
            ViewBag.TopTuyenDuong = topTuyenDuong;
            ViewBag.DanhGiaMoiNhat = danhGiaMoiNhat;

            return View();
        }

        // Quản lý người dùng - redirect to UserManagement
        public IActionResult QuanLyNguoiDung()
        {
            return RedirectToAction("Index", "UserManagement");
        }

        // Quản lý tuyến đường
        public IActionResult QuanLyTuyenDuong()
        {
            return RedirectToAction("Index", "TuyenDuong", new { area = "Admin" });
        }





        // Khoá/Mở khoá tài khoản người dùng (Deprecated - use UserManagementController)
        [HttpPost]
        public async Task<JsonResult> ToggleUserLock(int userId)
        {
            try
            {
                var user = await _context.NguoiDungs.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy người dùng" });
                }

                // Toggle trạng thái khoá tài khoản
                user.TaiKhoanBiKhoa = !user.TaiKhoanBiKhoa;
                user.NgayCapNhat = DateTime.Now;

                await _context.SaveChangesAsync();

                var status = user.TaiKhoanBiKhoa ? "khoá" : "mở khoá";
                return Json(new {
                    success = true,
                    message = $"Đã {status} tài khoản thành công",
                    isLocked = user.TaiKhoanBiKhoa
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Xem lịch sử giao dịch của người dùng
        public async Task<IActionResult> LichSuGiaoDich(int userId)
        {
            var user = await _context.NguoiDungs.FindAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction("QuanLyNguoiDung");
            }

            var lichSuGiaoDich = await _context.ThanhToans
                .Include(t => t.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(t => t.Ve.NguoiDungId == userId)
                .OrderByDescending(t => t.NgayThanhToan)
                .ToListAsync();

            ViewBag.User = user;
            return View(lichSuGiaoDich);
        }

        // Quản lý đánh giá - redirect to Review controller
        public IActionResult QuanLyDanhGia()
        {
            return RedirectToAction("Index", "Review");
        }

        // Ẩn/hiện đánh giá
        [HttpPost]
        public async Task<JsonResult> ToggleReviewVisibility(int reviewId)
        {
            try
            {
                var review = await _context.DanhGiaChuyenDis.FindAsync(reviewId);
                if (review == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đánh giá" });
                }

                review.BiAn = !review.BiAn;
                await _context.SaveChangesAsync();

                var status = review.BiAn ? "ẩn" : "hiện";
                return Json(new {
                    success = true,
                    message = $"Đã {status} đánh giá thành công",
                    isHidden = review.BiAn
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Quản lý khuyến mãi
        public async Task<IActionResult> QuanLyKhuyenMai()
        {
            var khuyenMais = await _context.KhuyenMais
                .OrderByDescending(k => k.NgayTao)
                .ToListAsync();

            return View(khuyenMais);
        }

        // Thêm tuyến đường mới
        public IActionResult ThemTuyenDuong()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ThemTuyenDuong([Bind("TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,GiaVe,TrangThaiHoatDong")] TuyenDuong tuyenDuong)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra tuyến đường đã tồn tại chưa
                if (await _context.TuyenDuongs.AnyAsync(t => t.DiemDi == tuyenDuong.DiemDi && t.DiemDen == tuyenDuong.DiemDen))
                {
                    ModelState.AddModelError("", "Tuyến đường từ " + tuyenDuong.DiemDi + " đến " + tuyenDuong.DiemDen + " đã tồn tại");
                    return View(tuyenDuong);
                }

                tuyenDuong.NgayTao = DateTime.Now;
                _context.Add(tuyenDuong);
                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Thêm tuyến đường thành công";
                return RedirectToAction(nameof(QuanLyTuyenDuong));
            }
            return View(tuyenDuong);
        }

        // Sửa tuyến đường
        public async Task<IActionResult> SuaTuyenDuong(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var tuyenDuong = await _context.TuyenDuongs.FindAsync(id);
            if (tuyenDuong == null)
            {
                return NotFound();
            }
            return View(tuyenDuong);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SuaTuyenDuong(int id, [Bind("TuyenDuongId,TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,GiaVe,TrangThaiHoatDong,NgayTao")] TuyenDuong tuyenDuong)
        {
            if (id != tuyenDuong.TuyenDuongId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Kiểm tra tuyến đường đã tồn tại (trừ tuyến hiện tại)
                    if (await _context.TuyenDuongs.AnyAsync(t => t.DiemDi == tuyenDuong.DiemDi && t.DiemDen == tuyenDuong.DiemDen && t.TuyenDuongId != tuyenDuong.TuyenDuongId))
                    {
                        ModelState.AddModelError("", "Tuyến đường từ " + tuyenDuong.DiemDi + " đến " + tuyenDuong.DiemDen + " đã tồn tại");
                        return View(tuyenDuong);
                    }

                    tuyenDuong.NgayCapNhat = DateTime.Now;
                    _context.Update(tuyenDuong);
                    await _context.SaveChangesAsync();
                    
                    TempData["Success"] = "Cập nhật tuyến đường thành công";
                    return RedirectToAction(nameof(QuanLyTuyenDuong));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TuyenDuongExists(tuyenDuong.TuyenDuongId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
            }
            return View(tuyenDuong);
        }

        // Xóa tuyến đường
        [HttpPost]
        public async Task<JsonResult> XoaTuyenDuong(int id)
        {
            try
            {
                var tuyenDuong = await _context.TuyenDuongs.FindAsync(id);
                if (tuyenDuong == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy tuyến đường" });
                }

                // Kiểm tra tuyến đường có đang được sử dụng trong chuyến xe nào không
                var hasChuyenXe = await _context.ChuyenXes.AnyAsync(c => c.TuyenDuongId == id);
                if (hasChuyenXe)
                {
                    return Json(new { success = false, message = "Không thể xóa tuyến đường đang có chuyến xe" });
                }

                _context.TuyenDuongs.Remove(tuyenDuong);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Xóa tuyến đường thành công" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        private bool TuyenDuongExists(int id)
        {
            return _context.TuyenDuongs.Any(e => e.TuyenDuongId == id);
        }

        // Duyệt chuyến xe mới
        public async Task<IActionResult> DuyetChuyenXe()
        {
            var chuyenXeChoDuyet = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .Where(c => c.TrangThaiDuyet == TrangThaiDuyet.ChoDuyet)
                .OrderByDescending(c => c.NgayTao)
                .ToListAsync();

            return View(chuyenXeChoDuyet);
        }

        // Phê duyệt chuyến xe
        [HttpPost]
        public async Task<JsonResult> PheDuyetChuyenXe(int chuyenXeId, bool pheDuyet, string? lyDo)
        {
            try
            {
                var chuyenXe = await _context.ChuyenXes.FindAsync(chuyenXeId);
                if (chuyenXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy chuyến xe" });
                }

                if (pheDuyet)
                {
                    chuyenXe.TrangThaiDuyet = TrangThaiDuyet.DaDuyet;
                    chuyenXe.TrangThaiChuyenXe = TrangThaiChuyenXe.HoatDong;
                }
                else
                {
                    chuyenXe.TrangThaiDuyet = TrangThaiDuyet.BiTuChoi;
                    chuyenXe.TrangThaiChuyenXe = TrangThaiChuyenXe.DaHuy;
                }

                chuyenXe.NgayDuyet = DateTime.Now;
                chuyenXe.LyDoTuChoi = lyDo;
                chuyenXe.NgayCapNhat = DateTime.Now;

                await _context.SaveChangesAsync();

                var message = pheDuyet ? "Phê duyệt chuyến xe thành công" : "Từ chối chuyến xe thành công";
                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Quản lý chuyến xe
        [Route("/Admin/QuanLyChuyenXe")]
        public async Task<IActionResult> QuanLyChuyenXe()
        {
            var chuyenXes = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .ToListAsync();
            return View(chuyenXes);
        }
    }
}
