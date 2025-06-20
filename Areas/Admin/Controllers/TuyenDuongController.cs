using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using DatVeXe.Attributes;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class TuyenDuongController : Controller
    {
        private readonly DatVeXeContext _context;
        public TuyenDuongController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/TuyenDuong
        public async Task<IActionResult> Index(string searchString, bool? trangThai, string sortOrder, int page = 1)
        {
            ViewBag.CurrentFilter = searchString;
            ViewBag.TrangThaiFilter = trangThai;
            ViewBag.CurrentSort = sortOrder;
            ViewBag.NameSortParm = string.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewBag.DistanceSortParm = sortOrder == "distance" ? "distance_desc" : "distance";
            ViewBag.PriceSortParm = sortOrder == "price" ? "price_desc" : "price";

            var tuyenDuongs = _context.TuyenDuongs
                .Include(t => t.ChuyenXes)
                .AsQueryable();

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                tuyenDuongs = tuyenDuongs.Where(t =>
                    t.TenTuyen.Contains(searchString) ||
                    t.DiemDi.Contains(searchString) ||
                    t.DiemDen.Contains(searchString));
            }

            // Lọc theo trạng thái
            if (trangThai.HasValue)
            {
                tuyenDuongs = tuyenDuongs.Where(t => t.TrangThaiHoatDong == trangThai.Value);
            }

            // Sắp xếp
            switch (sortOrder)
            {
                case "name_desc":
                    tuyenDuongs = tuyenDuongs.OrderByDescending(t => t.TenTuyen);
                    break;
                case "distance":
                    tuyenDuongs = tuyenDuongs.OrderBy(t => t.KhoangCach);
                    break;
                case "distance_desc":
                    tuyenDuongs = tuyenDuongs.OrderByDescending(t => t.KhoangCach);
                    break;
                case "price":
                    tuyenDuongs = tuyenDuongs.OrderBy(t => t.GiaVe);
                    break;
                case "price_desc":
                    tuyenDuongs = tuyenDuongs.OrderByDescending(t => t.GiaVe);
                    break;
                default:
                    tuyenDuongs = tuyenDuongs.OrderBy(t => t.TenTuyen);
                    break;
            }

            // Phân trang
            int pageSize = 10;
            var totalCount = await tuyenDuongs.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            var pagedTuyenDuongs = await tuyenDuongs
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pagedTuyenDuongs);
        }

        // GET: Admin/TuyenDuong/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var tuyenDuong = await _context.TuyenDuongs
                .Include(t => t.ChuyenXes)
                    .ThenInclude(c => c.Xe)
                .Include(t => t.ChuyenXes)
                    .ThenInclude(c => c.Ves)
                .FirstOrDefaultAsync(t => t.TuyenDuongId == id);

            if (tuyenDuong == null) return NotFound();

            // Thống kê tuyến đường
            var tongChuyenXe = tuyenDuong.ChuyenXes?.Count ?? 0;
            var tongVeDaBan = tuyenDuong.ChuyenXes?.Sum(c => c.Ves?.Count ?? 0) ?? 0;
            var doanhThu = await _context.ThanhToans
                .Where(t => t.Ve != null && t.Ve.ChuyenXe != null &&
                           t.Ve.ChuyenXe.TuyenDuongId == id &&
                           t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);

            // Chuyến xe gần đây
            var chuyenXeGanDay = tuyenDuong.ChuyenXes?
                .Where(c => c.NgayKhoiHanh >= DateTime.Now.AddDays(-30))
                .OrderByDescending(c => c.NgayKhoiHanh)
                .Take(5)
                .ToList();

            ViewBag.TongChuyenXe = tongChuyenXe;
            ViewBag.TongVeDaBan = tongVeDaBan;
            ViewBag.DoanhThu = doanhThu;
            ViewBag.ChuyenXeGanDay = chuyenXeGanDay;

            return View(tuyenDuong);
        }

        // GET: Admin/TuyenDuong/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/TuyenDuong/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,MoTa,TrangThaiHoatDong,GiaVe")] TuyenDuong tuyenDuong)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra tuyến đường đã tồn tại chưa
                var existingRoute = await _context.TuyenDuongs
                    .FirstOrDefaultAsync(t => t.DiemDi == tuyenDuong.DiemDi && t.DiemDen == tuyenDuong.DiemDen);

                if (existingRoute != null)
                {
                    ModelState.AddModelError("", $"Tuyến đường từ {tuyenDuong.DiemDi} đến {tuyenDuong.DiemDen} đã tồn tại");
                    return View(tuyenDuong);
                }

                tuyenDuong.NgayTao = DateTime.Now;
                _context.Add(tuyenDuong);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Tạo tuyến đường thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(tuyenDuong);
        }

        // GET: Admin/TuyenDuong/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var tuyenDuong = await _context.TuyenDuongs.FindAsync(id);
            if (tuyenDuong == null) return NotFound();
            return View(tuyenDuong);
        }

        // POST: Admin/TuyenDuong/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("TuyenDuongId,TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,MoTa,TrangThaiHoatDong,GiaVe")] TuyenDuong tuyenDuong)
        {
            if (id != tuyenDuong.TuyenDuongId) return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(tuyenDuong);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.TuyenDuongs.Any(e => e.TuyenDuongId == tuyenDuong.TuyenDuongId))
                        return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(tuyenDuong);
        }

        // GET: Admin/TuyenDuong/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var tuyenDuong = await _context.TuyenDuongs.FirstOrDefaultAsync(m => m.TuyenDuongId == id);
            if (tuyenDuong == null) return NotFound();
            return View(tuyenDuong);
        }

        // POST: Admin/TuyenDuong/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var tuyenDuong = await _context.TuyenDuongs.FindAsync(id);
            if (tuyenDuong != null)
            {
                // Kiểm tra có chuyến xe liên quan không
                bool hasChuyenXe = await _context.ChuyenXes.AnyAsync(c => c.TuyenDuongId == id);
                if (hasChuyenXe)
                {
                    TempData["Error"] = "Không thể xóa tuyến đường này vì đang có chuyến xe liên quan.";
                    return RedirectToAction("Delete", new { id });
                }
                _context.TuyenDuongs.Remove(tuyenDuong);
                await _context.SaveChangesAsync();

                TempData["Success"] = "Xóa tuyến đường thành công!";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Admin/TuyenDuong/ToggleStatus
        [HttpPost]
        public async Task<JsonResult> ToggleStatus(int tuyenDuongId)
        {
            try
            {
                var tuyenDuong = await _context.TuyenDuongs.FindAsync(tuyenDuongId);
                if (tuyenDuong == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy tuyến đường" });
                }

                tuyenDuong.TrangThaiHoatDong = !tuyenDuong.TrangThaiHoatDong;
                await _context.SaveChangesAsync();

                var status = tuyenDuong.TrangThaiHoatDong ? "kích hoạt" : "vô hiệu hóa";
                return Json(new {
                    success = true,
                    message = $"Đã {status} tuyến đường thành công",
                    isActive = tuyenDuong.TrangThaiHoatDong
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/TuyenDuong/ThongKe
        public async Task<IActionResult> ThongKe()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Thống kê tổng quan
            var tongTuyenDuong = await _context.TuyenDuongs.CountAsync();
            var tuyenHoatDong = await _context.TuyenDuongs.CountAsync(t => t.TrangThaiHoatDong);
            var tuyenKhongHoatDong = tongTuyenDuong - tuyenHoatDong;

            // Top tuyến đường có doanh thu cao nhất
            var topTuyenDoanhThu = await _context.ThanhToans
                .Include(t => t.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(t => t.TrangThai == TrangThaiThanhToan.ThanhCong &&
                           t.Ve != null && t.Ve.ChuyenXe != null && t.Ve.ChuyenXe.TuyenDuong != null)
                .GroupBy(t => new {
                    t.Ve.ChuyenXe.TuyenDuong.TuyenDuongId,
                    t.Ve.ChuyenXe.TuyenDuong.DiemDi,
                    t.Ve.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    DoanhThu = g.Sum(x => x.SoTien),
                    SoVe = g.Count()
                })
                .OrderByDescending(x => x.DoanhThu)
                .Take(5)
                .ToListAsync();

            // Thống kê theo khoảng cách
            var thongKeKhoangCach = await _context.TuyenDuongs
                .GroupBy(t => t.KhoangCach < 100 ? "Ngắn (<100km)" :
                             t.KhoangCach < 300 ? "Trung bình (100-300km)" : "Dài (>300km)")
                .Select(g => new {
                    LoaiTuyen = g.Key,
                    SoLuong = g.Count(),
                    TrungBinhGiaVe = g.Average(x => x.GiaVe)
                })
                .ToListAsync();

            ViewBag.TongTuyenDuong = tongTuyenDuong;
            ViewBag.TuyenHoatDong = tuyenHoatDong;
            ViewBag.TuyenKhongHoatDong = tuyenKhongHoatDong;
            ViewBag.TopTuyenDoanhThu = topTuyenDoanhThu;
            ViewBag.ThongKeKhoangCach = thongKeKhoangCach;

            return View();
        }
    }
}
