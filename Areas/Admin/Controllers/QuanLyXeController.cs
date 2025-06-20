using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using DatVeXe.Attributes;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class QuanLyXeController : Controller
    {
        private readonly DatVeXeContext _context;

        public QuanLyXeController(DatVeXeContext context)
        {
            _context = context;
        }



        // GET: Admin/QuanLyXe
        public async Task<IActionResult> Index(string searchString, string loaiXe, bool? trangThai, string sortOrder, int page = 1)
        {

            ViewBag.CurrentFilter = searchString;
            ViewBag.LoaiXeFilter = loaiXe;
            ViewBag.TrangThaiFilter = trangThai?.ToString().ToLower();
            ViewBag.CurrentSort = sortOrder;

            var xes = _context.Xes
                .Include(x => x.ChuyenXes)
                .AsQueryable();

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                xes = xes.Where(x => x.BienSoXe.Contains(searchString) ||
                                    x.LoaiXe.Contains(searchString) ||
                                    (x.MoTa != null && x.MoTa.Contains(searchString)));
            }

            // Lọc theo loại xe
            if (!string.IsNullOrEmpty(loaiXe))
            {
                xes = xes.Where(x => x.LoaiXe == loaiXe);
            }

            // Lọc theo trạng thái
            if (trangThai.HasValue)
            {
                xes = xes.Where(x => x.TrangThaiHoatDong == trangThai.Value);
            }

            // Sắp xếp
            switch (sortOrder)
            {
                case "bien_so_desc":
                    xes = xes.OrderByDescending(x => x.BienSoXe);
                    break;
                case "so_ghe":
                    xes = xes.OrderBy(x => x.SoGhe);
                    break;
                case "so_ghe_desc":
                    xes = xes.OrderByDescending(x => x.SoGhe);
                    break;
                default:
                    xes = xes.OrderBy(x => x.BienSoXe);
                    break;
            }

            // Phân trang
            int pageSize = 10;
            var totalCount = await xes.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            var pagedXes = await xes
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pagedXes);
        }

        // GET: Admin/QuanLyXe/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.FirstOrDefaultAsync(m => m.XeId == id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // GET: Admin/QuanLyXe/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/QuanLyXe/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BienSoXe,LoaiXe,SoGhe,MoTa")] Xe xe)
        {
            if (_context.Xes.Any(x => x.BienSoXe == xe.BienSoXe))
            {
                ModelState.AddModelError("BienSoXe", "Biển số xe đã tồn tại!");
            }
            if (ModelState.IsValid)
            {
                _context.Add(xe);
                await _context.SaveChangesAsync();
                TempData["ThongBao"] = "Thêm xe thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(xe);
        }

        // GET: Admin/QuanLyXe/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.FindAsync(id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // POST: Admin/QuanLyXe/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("XeId,BienSoXe,LoaiXe,SoGhe,MoTa")] Xe xe)
        {
            if (id != xe.XeId) return NotFound();
            if (_context.Xes.Any(x => x.BienSoXe == xe.BienSoXe && x.XeId != xe.XeId))
            {
                ModelState.AddModelError("BienSoXe", "Biển số xe đã tồn tại!");
            }
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(xe);
                    await _context.SaveChangesAsync();
                    TempData["ThongBao"] = "Cập nhật xe thành công!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!XeExists(xe.XeId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(xe);
        }

        // GET: Admin/QuanLyXe/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.Include(x => x.ChuyenXes).FirstOrDefaultAsync(m => m.XeId == id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // POST: Admin/QuanLyXe/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var xe = await _context.Xes.Include(x => x.ChuyenXes).FirstOrDefaultAsync(x => x.XeId == id);
            if (xe != null)
            {
                if (xe.ChuyenXes != null && xe.ChuyenXes.Count > 0)
                {
                    TempData["ThongBao"] = "Không thể xóa xe đã có chuyến!";
                    return RedirectToAction(nameof(Index));
                }
                _context.Xes.Remove(xe);
                await _context.SaveChangesAsync();
                TempData["ThongBao"] = "Xóa xe thành công!";
            }
            return RedirectToAction(nameof(Index));
        }

        // POST: Admin/QuanLyXe/ToggleStatus
        [HttpPost]
        public async Task<JsonResult> ToggleStatus(int xeId)
        {
            try
            {
                var xe = await _context.Xes.FindAsync(xeId);
                if (xe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy xe" });
                }

                xe.TrangThaiHoatDong = !xe.TrangThaiHoatDong;
                await _context.SaveChangesAsync();

                var status = xe.TrangThaiHoatDong ? "kích hoạt" : "vô hiệu hóa";
                return Json(new {
                    success = true,
                    message = $"Đã {status} xe thành công",
                    isActive = xe.TrangThaiHoatDong
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/QuanLyXe/ThongKe
        public async Task<IActionResult> ThongKe()
        {

            // Thống kê tổng quan
            var tongXe = await _context.Xes.CountAsync();
            var xeHoatDong = await _context.Xes.CountAsync(x => x.TrangThaiHoatDong);
            var xeKhongHoatDong = tongXe - xeHoatDong;

            // Thống kê theo loại xe
            var thongKeLoaiXe = await _context.Xes
                .GroupBy(x => x.LoaiXe)
                .Select(g => new {
                    LoaiXe = g.Key,
                    SoLuong = g.Count(),
                    TrungBinhSoGhe = g.Average(x => x.SoGhe)
                })
                .ToListAsync();

            // Top xe có nhiều chuyến nhất
            var topXeNhieuChuyen = await _context.Xes
                .Include(x => x.ChuyenXes)
                .Where(x => x.ChuyenXes.Any())
                .Select(x => new {
                    BienSoXe = x.BienSoXe,
                    LoaiXe = x.LoaiXe,
                    SoChuyenXe = x.ChuyenXes.Count,
                    TyLeSuDung = x.ChuyenXes.Count(c => c.NgayKhoiHanh < DateTime.Now) * 100.0 / x.ChuyenXes.Count
                })
                .OrderByDescending(x => x.SoChuyenXe)
                .Take(5)
                .ToListAsync();

            // Thống kê theo năm sản xuất
            var thongKeNamSanXuat = await _context.Xes
                .Where(x => x.NamSanXuat.HasValue)
                .GroupBy(x => x.NamSanXuat.Value)
                .Select(g => new {
                    NamSanXuat = g.Key,
                    SoLuong = g.Count()
                })
                .OrderBy(x => x.NamSanXuat)
                .ToListAsync();

            ViewBag.TongXe = tongXe;
            ViewBag.XeHoatDong = xeHoatDong;
            ViewBag.XeKhongHoatDong = xeKhongHoatDong;
            ViewBag.ThongKeLoaiXe = thongKeLoaiXe;
            ViewBag.TopXeNhieuChuyen = topXeNhieuChuyen;
            ViewBag.ThongKeNamSanXuat = thongKeNamSanXuat;

            return View();
        }

        private bool XeExists(int id)
        {
            return _context.Xes.Any(e => e.XeId == id);
        }
    }
}
