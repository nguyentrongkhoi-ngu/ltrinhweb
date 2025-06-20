using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace DatVeXe.Controllers
{
    public class TuyenDuongController : Controller
    {
        private readonly DatVeXeContext _context;

        public TuyenDuongController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: TuyenDuong
        public async Task<IActionResult> Index()
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            // Lấy danh sách tuyến đường từ bảng TuyenDuongs và thống kê từ ChuyenXes
            var tuyenDuongs = await _context.TuyenDuongs
                .Where(t => t.TrangThaiHoatDong)
                .Select(t => new TuyenDuongViewModel
                {
                    TuyenDuongId = t.TuyenDuongId, // Bổ sung dòng này
                    DiemDi = t.DiemDi,
                    DiemDen = t.DiemDen,
                    SoChuyenXe = _context.ChuyenXes.Count(c => c.TuyenDuongId == t.TuyenDuongId),
                    TongVeDaBan = _context.ChuyenXes
                        .Where(c => c.TuyenDuongId == t.TuyenDuongId)
                        .SelectMany(c => c.Ves)
                        .Count(),
                    DoanhThu = _context.ChuyenXes
                        .Where(c => c.TuyenDuongId == t.TuyenDuongId)
                        .SelectMany(c => c.Ves)
                        .Sum(v => v.GiaVe),
                    GiaThapNhat = _context.ChuyenXes
                        .Where(c => c.TuyenDuongId == t.TuyenDuongId)
                        .Min(c => (decimal?)c.Gia) ?? 0,
                    GiaCaoNhat = _context.ChuyenXes
                        .Where(c => c.TuyenDuongId == t.TuyenDuongId)
                        .Max(c => (decimal?)c.Gia) ?? 0,
                    ChuyenXeGanNhat = _context.ChuyenXes
                        .Where(c => c.TuyenDuongId == t.TuyenDuongId)
                        .Max(c => (DateTime?)c.NgayKhoiHanh) ?? DateTime.MinValue,
                    TrangThaiHoatDong = _context.ChuyenXes
                        .Any(c => c.TuyenDuongId == t.TuyenDuongId && c.NgayKhoiHanh > DateTime.Now)
                })
                .OrderByDescending(t => t.SoChuyenXe)
                .ToListAsync();

            return View(tuyenDuongs);
        }

        // GET: TuyenDuong/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            if (id == null)
            {
                return NotFound();
            }
            var tuyenDuong = await _context.TuyenDuongs.FirstOrDefaultAsync(t => t.TuyenDuongId == id);
            if (tuyenDuong == null)
            {
                return NotFound();
            }
            return View(tuyenDuong);
        }

        // GET: TuyenDuong/ThongKe
        public async Task<IActionResult> ThongKe()
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            var today = DateTime.Today;
            var thisMonth = new DateTime(today.Year, today.Month, 1);
            var nextMonth = thisMonth.AddMonths(1);

            // Thống kê tổng quan
            var tongTuyenDuong = await _context.ChuyenXes
                .Select(c => new { c.DiemDi, c.DiemDen })
                .Distinct()
                .CountAsync();

            var tuyenHoatDong = await _context.ChuyenXes
                .Where(c => c.NgayKhoiHanh > DateTime.Now)
                .Select(c => new { c.DiemDi, c.DiemDen })
                .Distinct()
                .CountAsync();

            // Top 10 tuyến đường phổ biến
            var topTuyenDuong = await _context.ChuyenXes
                .GroupBy(c => new { c.DiemDi, c.DiemDen })
                .Select(g => new
                {
                    TuyenDuong = g.Key.DiemDi + " - " + g.Key.DiemDen,
                    SoChuyenXe = g.Count(),
                    TongVe = g.Sum(c => c.Ves != null ? c.Ves.Count : 0),
                    DoanhThu = g.Sum(c => c.Ves != null ? c.Ves.Sum(v => v.GiaVe) : 0)
                })
                .OrderByDescending(t => t.TongVe)
                .Take(10)
                .ToListAsync();

            // Thống kê theo tháng
            var thongKeThang = await _context.ChuyenXes
                .Include(c => c.TuyenDuong)
                .Include(c => c.Ves)
                .Where(c => c.NgayKhoiHanh >= thisMonth && c.NgayKhoiHanh < nextMonth)
                .GroupBy(c => new { c.TuyenDuong.DiemDi, c.TuyenDuong.DiemDen })
                .Select(g => new
                {
                    TuyenDuong = g.Key.DiemDi + " - " + g.Key.DiemDen,
                    SoChuyenXe = g.Count(),
                    TongVe = g.Sum(c => c.Ves.Count),
                    DoanhThu = g.Sum(c => c.Ves.Sum(v => v.GiaVe))
                })
                .OrderByDescending(t => t.DoanhThu)
                .Take(5)
                .ToListAsync();

            ViewBag.TongTuyenDuong = tongTuyenDuong;
            ViewBag.TuyenHoatDong = tuyenHoatDong;
            ViewBag.TopTuyenDuong = topTuyenDuong;
            ViewBag.ThongKeThang = thongKeThang;

            return View();
        }

        // POST: TuyenDuong/CapNhatTrangThai
        [HttpPost]
        public async Task<IActionResult> CapNhatTrangThai(string diemDi, string diemDen, bool trangThai)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                return Json(new { success = false, message = "Không có quyền truy cập" });
            }

            try
            {
                var chuyenXes = await _context.ChuyenXes
                    .Where(c => c.DiemDi == diemDi && c.DiemDen == diemDen && c.NgayKhoiHanh > DateTime.Now)
                    .ToListAsync();

                var newStatus = trangThai ? TrangThaiChuyenXe.HoatDong : TrangThaiChuyenXe.TamDung;

                foreach (var chuyenXe in chuyenXes)
                {
                    chuyenXe.TrangThaiChuyenXe = newStatus;
                }

                await _context.SaveChangesAsync();

                var statusText = trangThai ? "hoạt động" : "tạm dừng";
                return Json(new {
                    success = true,
                    message = $"Đã cập nhật {chuyenXes.Count} chuyến xe thành trạng thái {statusText}"
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: TuyenDuong/Create
        public IActionResult Create()
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        // POST: TuyenDuong/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,MoTa,TrangThaiHoatDong,GiaVe")] TuyenDuong tuyenDuong)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            if (ModelState.IsValid)
            {
                _context.Add(tuyenDuong);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(tuyenDuong);
        }

        // GET: TuyenDuong/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
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

        // POST: TuyenDuong/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("TuyenDuongId,TenTuyen,DiemDi,DiemDen,KhoangCach,ThoiGianDuKien,MoTa,TrangThaiHoatDong,GiaVe")] TuyenDuong tuyenDuong)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            if (id != tuyenDuong.TuyenDuongId)
            {
                return NotFound();
            }
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
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                return RedirectToAction(nameof(Index));
            }
            return View(tuyenDuong);
        }

        // GET: TuyenDuong/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            if (id == null)
            {
                return NotFound();
            }
            var tuyenDuong = await _context.TuyenDuongs
                .FirstOrDefaultAsync(m => m.TuyenDuongId == id);
            if (tuyenDuong == null)
            {
                return NotFound();
            }
            return View(tuyenDuong);
        }

        // POST: TuyenDuong/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }
            var tuyenDuong = await _context.TuyenDuongs.FindAsync(id);
            if (tuyenDuong != null)
            {
                _context.TuyenDuongs.Remove(tuyenDuong);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }
    }
}
