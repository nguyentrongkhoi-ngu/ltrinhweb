using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Attributes;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Route("Admin/[controller]/[action]")]
    [AdminAuthorization]
    public class ChuyenXeController : Controller
    {
        private readonly DatVeXeContext _context;
        public ChuyenXeController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/ChuyenXe
        [HttpGet]
        [Route("/Admin/QuanLyChuyenXe")]
        public async Task<IActionResult> Index(string searchString, string trangThai, DateTime? tuNgay, DateTime? denNgay, int page = 1)
        {
            ViewBag.CurrentFilter = searchString;
            ViewBag.TrangThaiFilter = trangThai;
            ViewBag.TuNgayFilter = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgayFilter = denNgay?.ToString("yyyy-MM-dd");

            var chuyenXes = _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .Include(c => c.Ves)
                .AsQueryable();

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                chuyenXes = chuyenXes.Where(c =>
                    c.TuyenDuong.DiemDi.Contains(searchString) ||
                    c.TuyenDuong.DiemDen.Contains(searchString) ||
                    c.Xe.BienSoXe.Contains(searchString));
            }

            // Lọc theo trạng thái
            if (!string.IsNullOrEmpty(trangThai))
            {
                switch (trangThai)
                {
                    case "ChoDuyet":
                        chuyenXes = chuyenXes.Where(c => c.TrangThaiDuyet == TrangThaiDuyet.ChoDuyet);
                        break;
                    case "DaDuyet":
                        chuyenXes = chuyenXes.Where(c => c.TrangThaiDuyet == TrangThaiDuyet.DaDuyet);
                        break;
                    case "TuChoi":
                        chuyenXes = chuyenXes.Where(c => c.TrangThaiDuyet == TrangThaiDuyet.BiTuChoi);
                        break;
                }
            }

            // Lọc theo ngày
            if (tuNgay.HasValue)
            {
                chuyenXes = chuyenXes.Where(c => c.NgayKhoiHanh.Date >= tuNgay.Value.Date);
            }
            if (denNgay.HasValue)
            {
                chuyenXes = chuyenXes.Where(c => c.NgayKhoiHanh.Date <= denNgay.Value.Date);
            }

            // Phân trang
            int pageSize = 15;
            var totalCount = await chuyenXes.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            var pagedChuyenXes = await chuyenXes
                .OrderByDescending(c => c.NgayTao)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View("~/Areas/Admin/Views/Admin/QuanLyChuyenXe.cshtml", pagedChuyenXes);
        }

        // GET: Admin/ChuyenXe/Details/5
        [HttpGet]
        [Route("Details/{id:int}")]
        public async Task<IActionResult> Details(int id)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);
            if (chuyenXe == null) return NotFound();
            return View(chuyenXe);
        }

        // GET: Admin/ChuyenXe/Create
        [HttpGet]
        [Route("Create")]
        public IActionResult Create()
        {
            ViewBag.Xes = _context.Xes.Where(x => x.TrangThaiHoatDong).ToList();
            ViewBag.TuyenDuongs = _context.TuyenDuongs.Where(t => t.TrangThaiHoatDong).ToList();
            ViewBag.TaiXes = _context.TaiXes.Where(tx => tx.TrangThai == TrangThaiTaiXe.HoatDong).ToList();
            return View();
        }

        // POST: Admin/ChuyenXe/Create
        [HttpPost]
        [Route("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("DiemDi,DiemDen,NgayKhoiHanh,ThoiGianDi,Gia,XeId,TuyenDuongId,TaiXeId")] ChuyenXe chuyenXe)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra xe có bị trùng lịch không
                var conflictTrip = await _context.ChuyenXes
                    .Where(c => c.XeId == chuyenXe.XeId &&
                               c.NgayKhoiHanh.Date == chuyenXe.NgayKhoiHanh.Date &&
                               c.ThoiGianDi == chuyenXe.ThoiGianDi &&
                               c.TrangThaiDuyet != TrangThaiDuyet.BiTuChoi)
                    .FirstOrDefaultAsync();

                if (conflictTrip != null)
                {
                    ModelState.AddModelError("", "Xe đã có lịch trình vào thời gian này");
                }
                else
                {
                    chuyenXe.NgayTao = DateTime.Now;
                    chuyenXe.TrangThaiDuyet = TrangThaiDuyet.ChoDuyet;
                    _context.Add(chuyenXe);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Tạo chuyến xe thành công! Chuyến xe đang chờ duyệt.";
                    return RedirectToAction(nameof(Index));
                }
            }

            ViewBag.Xes = _context.Xes.Where(x => x.TrangThaiHoatDong).ToList();
            ViewBag.TuyenDuongs = _context.TuyenDuongs.Where(t => t.TrangThaiHoatDong).ToList();
            ViewBag.TaiXes = _context.TaiXes.Where(tx => tx.TrangThai == TrangThaiTaiXe.HoatDong).ToList();
            return View(chuyenXe);
        }

        // GET: Admin/ChuyenXe/Edit/5
        [HttpGet]
        [Route("Edit/{id:int}")]
        public async Task<IActionResult> Edit(int id)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.TaiXe)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null) return NotFound();

            ViewBag.Xes = _context.Xes.Where(x => x.TrangThaiHoatDong).ToList();
            ViewBag.TuyenDuongs = _context.TuyenDuongs.Where(t => t.TrangThaiHoatDong).ToList();
            ViewBag.TaiXes = _context.TaiXes.Where(tx => tx.TrangThai == TrangThaiTaiXe.HoatDong).ToList();
            return View(chuyenXe);
        }

        // POST: Admin/ChuyenXe/Edit/5
        [HttpPost]
        [Route("Edit/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("ChuyenXeId,DiemDi,DiemDen,NgayKhoiHanh,ThoiGianDi,Gia,XeId,TuyenDuongId,TaiXeId,GhiChu")] ChuyenXe chuyenXe)
        {
            if (id != chuyenXe.ChuyenXeId) return NotFound();

            // Remove validation for NgayCapNhat since it's NotMapped
            ModelState.Remove("NgayCapNhat");

            if (ModelState.IsValid)
            {
                try
                {
                    // Kiểm tra xe có bị trùng lịch không (trừ chuyến hiện tại)
                    var conflictTrip = await _context.ChuyenXes
                        .Where(c => c.XeId == chuyenXe.XeId &&
                                   c.NgayKhoiHanh.Date == chuyenXe.NgayKhoiHanh.Date &&
                                   c.ThoiGianDi == chuyenXe.ThoiGianDi &&
                                   c.ChuyenXeId != chuyenXe.ChuyenXeId &&
                                   c.TrangThaiDuyet != TrangThaiDuyet.BiTuChoi)
                        .FirstOrDefaultAsync();

                    if (conflictTrip != null)
                    {
                        ModelState.AddModelError("", "Xe đã có lịch trình vào thời gian này");
                        ViewBag.Xes = _context.Xes.Where(x => x.TrangThaiHoatDong).ToList();
                        ViewBag.TuyenDuongs = _context.TuyenDuongs.Where(t => t.TrangThaiHoatDong).ToList();
                        ViewBag.TaiXes = _context.TaiXes.Where(tx => tx.TrangThai == TrangThaiTaiXe.HoatDong).ToList();
                        return View(chuyenXe);
                    }

                    // Lấy thông tin chuyến xe hiện tại từ database
                    var existingChuyenXe = await _context.ChuyenXes.FindAsync(id);
                    if (existingChuyenXe == null) return NotFound();

                    // Cập nhật thông tin
                    existingChuyenXe.DiemDi = chuyenXe.DiemDi;
                    existingChuyenXe.DiemDen = chuyenXe.DiemDen;
                    existingChuyenXe.NgayKhoiHanh = chuyenXe.NgayKhoiHanh;
                    existingChuyenXe.ThoiGianDi = chuyenXe.ThoiGianDi;
                    existingChuyenXe.Gia = chuyenXe.Gia;
                    existingChuyenXe.XeId = chuyenXe.XeId;
                    existingChuyenXe.TuyenDuongId = chuyenXe.TuyenDuongId;
                    existingChuyenXe.TaiXeId = chuyenXe.TaiXeId;
                    existingChuyenXe.GhiChu = chuyenXe.GhiChu;
                    // NgayCapNhat is NotMapped, so we don't update it

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật chuyến xe thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.ChuyenXes.Any(e => e.ChuyenXeId == id))
                        return NotFound();
                    else throw;
                }
            }
            ViewBag.Xes = _context.Xes.Where(x => x.TrangThaiHoatDong).ToList();
            ViewBag.TuyenDuongs = _context.TuyenDuongs.Where(t => t.TrangThaiHoatDong).ToList();
            ViewBag.TaiXes = _context.TaiXes.Where(tx => tx.TrangThai == TrangThaiTaiXe.HoatDong).ToList();
            return View(chuyenXe);
        }

        // POST: Admin/ChuyenXe/Delete/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Ves)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null) return NotFound();

            // Kiểm tra có vé đã bán chưa
            if (chuyenXe.Ves != null && chuyenXe.Ves.Any())
            {
                TempData["Error"] = "Không thể xóa chuyến xe đã có vé được bán!";
                return RedirectToAction(nameof(Index));
            }

            _context.ChuyenXes.Remove(chuyenXe);
            await _context.SaveChangesAsync();

            TempData["Success"] = "Xóa chuyến xe thành công!";
            return RedirectToAction(nameof(Index));
        }

        // POST: Admin/ChuyenXe/DuyetChuyenXe
        [HttpPost]
        public async Task<JsonResult> DuyetChuyenXe(int chuyenXeId, bool duyet, string? lyDoTuChoi)
        {
            try
            {
                var chuyenXe = await _context.ChuyenXes.FindAsync(chuyenXeId);
                if (chuyenXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy chuyến xe" });
                }

                if (duyet)
                {
                    chuyenXe.TrangThaiDuyet = TrangThaiDuyet.DaDuyet;
                    chuyenXe.NgayDuyet = DateTime.Now;
                    chuyenXe.LyDoTuChoi = null;
                }
                else
                {
                    chuyenXe.TrangThaiDuyet = TrangThaiDuyet.BiTuChoi;
                    chuyenXe.NgayDuyet = DateTime.Now;
                    chuyenXe.LyDoTuChoi = lyDoTuChoi;
                }

                // NgayCapNhat is NotMapped, so we don't update it
                await _context.SaveChangesAsync();

                var message = duyet ? "Duyệt chuyến xe thành công" : "Từ chối chuyến xe thành công";
                return Json(new { success = true, message = message });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/ChuyenXe/ThongKe
        [HttpGet]
        public async Task<IActionResult> ThongKe()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Thống kê tổng quan
            var tongChuyenXe = await _context.ChuyenXes.CountAsync();
            var chuyenXeChoDuyet = await _context.ChuyenXes.CountAsync(c => c.TrangThaiDuyet == TrangThaiDuyet.ChoDuyet);
            var chuyenXeDaDuyet = await _context.ChuyenXes.CountAsync(c => c.TrangThaiDuyet == TrangThaiDuyet.DaDuyet);
            var chuyenXeTuChoi = await _context.ChuyenXes.CountAsync(c => c.TrangThaiDuyet == TrangThaiDuyet.BiTuChoi);

            // Chuyến xe trong tháng
            var chuyenXeThangNay = await _context.ChuyenXes
                .Where(c => c.NgayKhoiHanh >= startOfMonth && c.NgayKhoiHanh <= endOfMonth)
                .CountAsync();

            // Top tuyến đường có nhiều chuyến nhất
            var topTuyenDuong = await _context.ChuyenXes
                .Include(c => c.TuyenDuong)
                .Where(c => c.TrangThaiDuyet == TrangThaiDuyet.DaDuyet)
                .Where(c => c.TuyenDuong != null)
                .GroupBy(c => new { c.TuyenDuong.DiemDi, c.TuyenDuong.DiemDen })
                .Select(g => new {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    SoChuyenXe = g.Count()
                })
                .OrderByDescending(x => x.SoChuyenXe)
                .Take(5)
                .ToListAsync();

            ViewBag.TongChuyenXe = tongChuyenXe;
            ViewBag.ChuyenXeChoDuyet = chuyenXeChoDuyet;
            ViewBag.ChuyenXeDaDuyet = chuyenXeDaDuyet;
            ViewBag.ChuyenXeTuChoi = chuyenXeTuChoi;
            ViewBag.ChuyenXeThangNay = chuyenXeThangNay;
            ViewBag.TopTuyenDuong = topTuyenDuong;

            return View();
        }
    }
}
