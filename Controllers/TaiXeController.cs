using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;

namespace DatVeXe.Controllers
{
    public class TaiXeController : Controller
    {
        private readonly DatVeXeContext _context;

        public TaiXeController(DatVeXeContext context)
        {
            _context = context;
        }

        // Kiểm tra quyền admin
        private bool IsAdmin()
        {
            return HttpContext.Session.GetInt32("IsAdmin") == 1;
        }

        // Kiểm tra đã đăng nhập
        private bool IsLoggedIn()
        {
            return HttpContext.Session.GetInt32("UserId") != null;
        }

        // GET: TaiXe
        public async Task<IActionResult> Index(string searchString, int? trangThai, int page = 1)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }
            ViewData["CurrentFilter"] = searchString;
            ViewData["CurrentTrangThai"] = trangThai;

            var taiXes = from t in _context.TaiXes select t;

            if (!string.IsNullOrEmpty(searchString))
            {
                taiXes = taiXes.Where(t => t.HoTen.Contains(searchString) 
                                        || t.SoDienThoai.Contains(searchString)
                                        || t.CMND.Contains(searchString)
                                        || t.SoBangLai.Contains(searchString));
            }

            if (trangThai.HasValue)
            {
                taiXes = taiXes.Where(t => (int)t.TrangThai == trangThai.Value);
            }

            int pageSize = 10;
            var totalItems = await taiXes.CountAsync();
            var items = await taiXes
                .OrderBy(t => t.HoTen)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            ViewBag.TotalItems = totalItems;

            return View(items);
        }

        // GET: TaiXe/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            if (id == null)
            {
                return NotFound();
            }

            var taiXe = await _context.TaiXes
                .Include(t => t.ChuyenXes)
                    .ThenInclude(c => c.TuyenDuong)
                .FirstOrDefaultAsync(m => m.TaiXeId == id);

            if (taiXe == null)
            {
                return NotFound();
            }

            return View(taiXe);
        }

        // GET: TaiXe/Create
        public IActionResult Create()
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            return View();
        }

        // POST: TaiXe/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("HoTen,SoDienThoai,DiaChi,NgaySinh,GioiTinh,CMND,SoBangLai,LoaiBangLai,NgayCapBangLai,NgayHetHanBangLai,KinhNghiem,GhiChu,TrangThai,NgayVaoLam")] TaiXe taiXe)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }
            if (ModelState.IsValid)
            {
                // Kiểm tra trùng CMND
                if (await _context.TaiXes.AnyAsync(t => t.CMND == taiXe.CMND))
                {
                    ModelState.AddModelError("CMND", "Số CMND đã tồn tại trong hệ thống");
                    return View(taiXe);
                }

                // Kiểm tra trùng số bằng lái
                if (await _context.TaiXes.AnyAsync(t => t.SoBangLai == taiXe.SoBangLai))
                {
                    ModelState.AddModelError("SoBangLai", "Số bằng lái đã tồn tại trong hệ thống");
                    return View(taiXe);
                }

                taiXe.NgayTao = DateTime.Now;
                _context.Add(taiXe);
                await _context.SaveChangesAsync();
                
                TempData["SuccessMessage"] = "Thêm tài xế thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(taiXe);
        }

        // GET: TaiXe/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            if (id == null)
            {
                return NotFound();
            }

            var taiXe = await _context.TaiXes.FindAsync(id);
            if (taiXe == null)
            {
                return NotFound();
            }
            return View(taiXe);
        }

        // POST: TaiXe/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("TaiXeId,HoTen,SoDienThoai,DiaChi,NgaySinh,GioiTinh,CMND,SoBangLai,LoaiBangLai,NgayCapBangLai,NgayHetHanBangLai,KinhNghiem,GhiChu,TrangThai,NgayVaoLam,NgayTao")] TaiXe taiXe)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            if (id != taiXe.TaiXeId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Kiểm tra trùng CMND (trừ chính nó)
                    if (await _context.TaiXes.AnyAsync(t => t.CMND == taiXe.CMND && t.TaiXeId != taiXe.TaiXeId))
                    {
                        ModelState.AddModelError("CMND", "Số CMND đã tồn tại trong hệ thống");
                        return View(taiXe);
                    }

                    // Kiểm tra trùng số bằng lái (trừ chính nó)
                    if (await _context.TaiXes.AnyAsync(t => t.SoBangLai == taiXe.SoBangLai && t.TaiXeId != taiXe.TaiXeId))
                    {
                        ModelState.AddModelError("SoBangLai", "Số bằng lái đã tồn tại trong hệ thống");
                        return View(taiXe);
                    }

                    taiXe.NgayCapNhat = DateTime.Now;
                    _context.Update(taiXe);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Cập nhật thông tin tài xế thành công!";
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!TaiXeExists(taiXe.TaiXeId))
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
            return View(taiXe);
        }

        // GET: TaiXe/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }

            if (id == null)
            {
                return NotFound();
            }

            var taiXe = await _context.TaiXes
                .FirstOrDefaultAsync(m => m.TaiXeId == id);
            if (taiXe == null)
            {
                return NotFound();
            }

            return View(taiXe);
        }

        // POST: TaiXe/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return RedirectToAction("Auth", "TaiKhoan");
            }
            var taiXe = await _context.TaiXes.FindAsync(id);
            if (taiXe != null)
            {
                // Kiểm tra xem tài xế có đang được sử dụng trong chuyến xe nào không
                var hasChuyenXe = await _context.ChuyenXes.AnyAsync(c => c.TaiXeId == id);
                if (hasChuyenXe)
                {
                    TempData["ErrorMessage"] = "Không thể xóa tài xế này vì đang có chuyến xe liên quan!";
                    return RedirectToAction(nameof(Index));
                }

                _context.TaiXes.Remove(taiXe);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Xóa tài xế thành công!";
            }

            return RedirectToAction(nameof(Index));
        }

        private bool TaiXeExists(int id)
        {
            return _context.TaiXes.Any(e => e.TaiXeId == id);
        }

        // API để lấy danh sách tài xế cho dropdown
        [HttpGet]
        public async Task<JsonResult> GetTaiXeList()
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { error = "Unauthorized" });
            }
            var taiXes = await _context.TaiXes
                .Where(t => t.TrangThai == TrangThaiTaiXe.HoatDong)
                .Select(t => new {
                    value = t.TaiXeId,
                    text = $"{t.HoTen} - {t.SoDienThoai}"
                })
                .ToListAsync();

            return Json(taiXes);
        }

        // Cập nhật trạng thái tài xế
        [HttpPost]
        public async Task<JsonResult> UpdateTrangThai(int id, TrangThaiTaiXe trangThai)
        {
            if (!IsLoggedIn() || !IsAdmin())
            {
                return Json(new { success = false, message = "Unauthorized" });
            }

            try
            {
                var taiXe = await _context.TaiXes.FindAsync(id);
                if (taiXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy tài xế" });
                }

                taiXe.TrangThai = trangThai;
                taiXe.NgayCapNhat = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Cập nhật trạng thái thành công" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }
    }
}
