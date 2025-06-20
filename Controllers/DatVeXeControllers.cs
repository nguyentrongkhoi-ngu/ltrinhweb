using DatVeXe.Models;
using DatVeXe.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace DatVeXe.Controllers
{
    public class XeController : Controller
    {
        private readonly DatVeXeContext _context;
        public XeController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Xe
        public async Task<IActionResult> Index()
        {
            var xes = await _context.Xes
                .Include(x => x.ChuyenXes)
                .OrderBy(x => x.BienSo)
                .ToListAsync();
            return View(xes);
        }

        // GET: Xe/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            var xe = await _context.Xes
                .Include(x => x.ChuyenXes)
                .ThenInclude(c => c.Ves)
                .FirstOrDefaultAsync(x => x.XeId == id);

            if (xe == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            return View(xe);
        }

        // GET: Xe/Create
        public IActionResult Create()
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // POST: Xe/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BienSo,LoaiXe,SoGhe")] Xe xe)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (ModelState.IsValid)
            {
                // Check if license plate already exists
                if (await _context.Xes.AnyAsync(x => x.BienSo == xe.BienSo))
                {
                    ModelState.AddModelError("BienSo", "Biển số xe đã tồn tại");
                    return View(xe);
                }

                try
                {
                    _context.Add(xe);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Thêm xe thành công";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi thêm xe";
                }
            }

            return View(xe);
        }

        // GET: Xe/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            var xe = await _context.Xes.FindAsync(id);
            if (xe == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            return View(xe);
        }

        // POST: Xe/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("XeId,BienSo,LoaiXe,SoGhe")] Xe xe)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id != xe.XeId)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            if (ModelState.IsValid)
            {
                // Check if license plate already exists (excluding current record)
                if (await _context.Xes.AnyAsync(x => x.BienSo == xe.BienSo && x.XeId != xe.XeId))
                {
                    ModelState.AddModelError("BienSo", "Biển số xe đã tồn tại");
                    return View(xe);
                }

                try
                {
                    _context.Update(xe);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật xe thành công";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await XeExists(xe.XeId))
                    {
                        TempData["Error"] = "Không tìm thấy xe";
                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi cập nhật xe";
                }
            }

            return View(xe);
        }

        // GET: Xe/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            var xe = await _context.Xes
                .Include(x => x.ChuyenXes)
                .FirstOrDefaultAsync(x => x.XeId == id);

            if (xe == null)
            {
                TempData["Error"] = "Không tìm thấy xe";
                return RedirectToAction(nameof(Index));
            }

            return View(xe);
        }

        // POST: Xe/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            var xe = await _context.Xes
                .Include(x => x.ChuyenXes)
                .FirstOrDefaultAsync(x => x.XeId == id);

            if (xe != null)
            {
                // Check if xe has any trips
                if (xe.ChuyenXes != null && xe.ChuyenXes.Any())
                {
                    TempData["Error"] = "Không thể xóa xe đã có chuyến đi";
                    return RedirectToAction(nameof(Index));
                }

                try
                {
                    _context.Xes.Remove(xe);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Xóa xe thành công";
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi xóa xe";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> XeExists(int id)
        {
            return await _context.Xes.AnyAsync(e => e.XeId == id);
        }
    }

    public class ChuyenXeController : Controller
    {
        private readonly DatVeXeContext _context;
        public ChuyenXeController(DatVeXeContext context)
        {
            _context = context;
        }



        public async Task<IActionResult> Index(ChuyenXeSearchViewModel? searchModel)
        {
            try
            {
                var query = _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.Ves)
                    .Include(c => c.TuyenDuong)
                    .Include(c => c.TaiXe)
                    .AsQueryable();

                // Áp dụng các bộ lọc nếu có
                if (searchModel != null)
                {
                    if (!string.IsNullOrEmpty(searchModel.DiemDi))
                    {
                        query = query.Where(c => (c.TuyenDuong != null ? c.TuyenDuong.DiemDi : c.DiemDi ?? "").Contains(searchModel.DiemDi));
                    }

                    if (!string.IsNullOrEmpty(searchModel.DiemDen))
                    {
                        query = query.Where(c => (c.TuyenDuong != null ? c.TuyenDuong.DiemDen : c.DiemDen ?? "").Contains(searchModel.DiemDen));
                    }

                    if (searchModel.TuNgay.HasValue)
                    {
                        query = query.Where(c => c.NgayKhoiHanh.Date >= searchModel.TuNgay.Value.Date);
                    }

                    if (searchModel.DenNgay.HasValue)
                    {
                        query = query.Where(c => c.NgayKhoiHanh.Date <= searchModel.DenNgay.Value.Date);
                    }

                    // Lọc theo giá
                    if (searchModel.GiaTu.HasValue)
                    {
                        query = query.Where(c => c.Gia >= searchModel.GiaTu.Value);
                    }

                    if (searchModel.GiaDen.HasValue)
                    {
                        query = query.Where(c => c.Gia <= searchModel.GiaDen.Value);
                    }

                    // Lọc theo loại xe
                    if (!string.IsNullOrEmpty(searchModel.LoaiXe))
                    {
                        query = query.Where(c => c.Xe != null && c.Xe.LoaiXe.Contains(searchModel.LoaiXe));
                    }

                    if (!string.IsNullOrEmpty(searchModel.TrangThai))
                    {
                        switch (searchModel.TrangThai)
                        {
                            case "chua_khoi_hanh":
                                query = query.Where(c => c.NgayKhoiHanh > DateTime.Now);
                                break;
                            case "da_khoi_hanh":
                                query = query.Where(c => c.NgayKhoiHanh <= DateTime.Now);
                                break;
                            // "all" - không lọc gì
                        }
                    }
                }

                // Áp dụng sắp xếp
                var sortBy = searchModel?.SortBy ?? "NgayKhoiHanh";
                var sortOrder = searchModel?.SortOrder ?? "asc";

                switch (sortBy.ToLower())
                {
                    case "gia":
                        query = sortOrder == "desc" ? query.OrderByDescending(c => c.Gia) : query.OrderBy(c => c.Gia);
                        break;
                    case "diemdi":
                        query = sortOrder == "desc" ? query.OrderByDescending(c => c.TuyenDuong != null ? c.TuyenDuong.DiemDi : c.DiemDi ?? "") : query.OrderBy(c => c.TuyenDuong != null ? c.TuyenDuong.DiemDi : c.DiemDi ?? "");
                        break;
                    case "diemden":
                        query = sortOrder == "desc" ? query.OrderByDescending(c => c.TuyenDuong != null ? c.TuyenDuong.DiemDen : c.DiemDen ?? "") : query.OrderBy(c => c.TuyenDuong != null ? c.TuyenDuong.DiemDen : c.DiemDen ?? "");
                        break;
                    case "loaixe":
                        query = sortOrder == "desc" ? query.OrderByDescending(c => c.Xe != null ? c.Xe.LoaiXe : "") : query.OrderBy(c => c.Xe != null ? c.Xe.LoaiXe : "");
                        break;
                    default: // NgayKhoiHanh
                        query = sortOrder == "desc" ? query.OrderByDescending(c => c.NgayKhoiHanh) : query.OrderBy(c => c.NgayKhoiHanh);
                        break;
                }

                var chuyens = await query.ToListAsync();

                // Lọc theo ghế trống nếu cần (phải làm sau khi load data vì phức tạp)
                if (searchModel?.CoGheTrong == true)
                {
                    chuyens = chuyens.Where(c => c.Xe != null && c.Xe.SoGhe > (c.Ves?.Count ?? 0)).ToList();
                }

                // Tạo ViewModel để trả về
                var viewModel = new ChuyenXeSearchViewModel
                {
                    DiemDi = searchModel?.DiemDi,
                    DiemDen = searchModel?.DiemDen,
                    TuNgay = searchModel?.TuNgay,
                    DenNgay = searchModel?.DenNgay,
                    GiaTu = searchModel?.GiaTu,
                    GiaDen = searchModel?.GiaDen,
                    LoaiXe = searchModel?.LoaiXe,
                    TrangThai = searchModel?.TrangThai,
                    CoGheTrong = searchModel?.CoGheTrong,
                    SortBy = searchModel?.SortBy ?? "NgayKhoiHanh",
                    SortOrder = searchModel?.SortOrder ?? "asc",
                    KetQua = chuyens
                };

                return View(viewModel);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Có lỗi xảy ra khi tải dữ liệu: " + ex.Message;
                return View(new ChuyenXeSearchViewModel { KetQua = new List<ChuyenXe>() });
            }
        }

        // Action để export Excel
        public async Task<IActionResult> ExportExcel(ChuyenXeSearchViewModel? searchModel)
        {
            var query = _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.Ves)
                .Include(c => c.TuyenDuong)
                .AsQueryable();

            // Áp dụng các bộ lọc giống như Index
            if (searchModel != null)
            {
                if (!string.IsNullOrEmpty(searchModel.DiemDi))
                {
                    query = query.Where(c => (c.TuyenDuong != null ? c.TuyenDuong.DiemDi : c.DiemDi).Contains(searchModel.DiemDi));
                }

                if (!string.IsNullOrEmpty(searchModel.DiemDen))
                {
                    query = query.Where(c => (c.TuyenDuong != null ? c.TuyenDuong.DiemDen : c.DiemDen).Contains(searchModel.DiemDen));
                }

                if (searchModel.TuNgay.HasValue)
                {
                    query = query.Where(c => c.NgayKhoiHanh.Date >= searchModel.TuNgay.Value.Date);
                }

                if (searchModel.DenNgay.HasValue)
                {
                    query = query.Where(c => c.NgayKhoiHanh.Date <= searchModel.DenNgay.Value.Date);
                }

                if (!string.IsNullOrEmpty(searchModel.TrangThai))
                {
                    switch (searchModel.TrangThai)
                    {
                        case "chua_khoi_hanh":
                            query = query.Where(c => c.NgayKhoiHanh > DateTime.Now);
                            break;
                        case "da_khoi_hanh":
                            query = query.Where(c => c.NgayKhoiHanh <= DateTime.Now);
                            break;
                    }
                }
            }

            var chuyens = await query.OrderBy(c => c.NgayKhoiHanh).ToListAsync();

            // Lọc theo ghế trống nếu cần
            if (searchModel?.CoGheTrong == true)
            {
                chuyens = chuyens.Where(c => c.Xe.SoGhe > c.Ves.Count).ToList();
            }

            // Tạo CSV content
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Điểm đi,Điểm đến,Ngày khởi hành,Biển số xe,Loại xe,Số ghế,Số ghế trống,Trạng thái");

            foreach (var item in chuyens)
            {
                var soGheTrong = item.Xe.SoGhe - item.Ves.Count;
                var trangThai = item.NgayKhoiHanh <= DateTime.Now ? "Đã khởi hành" : "Chưa khởi hành";

                csv.AppendLine($"{item.DiemDiDisplay},{item.DiemDenDisplay},{item.NgayKhoiHanh:dd/MM/yyyy HH:mm},{item.Xe.BienSo},{item.Xe.LoaiXe},{item.Xe.SoGhe},{soGheTrong},{trangThai}");
            }

            var fileName = $"DanhSachChuyenXe_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());

            return File(bytes, "text/csv", fileName);
        }



        // ❌ DEPRECATED: Action cho tìm kiếm nâng cao - Sử dụng BookingController.Search thay thế
        // Giữ lại để tương thích với các link cũ, redirect đến BookingController
        public IActionResult Search()
        {
            return RedirectToAction("Search", "Booking");
        }

        // ❌ DEPRECATED: POST Search - Redirect đến BookingController
        [HttpPost]
        public IActionResult Search(ChuyenXeSearchViewModel searchModel)
        {
            return RedirectToAction("Search", "Booking");
        }

        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction(nameof(Index));
            }

            try
            {
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .ThenInclude(x => x.ChoNgois)
                    .Include(c => c.Ves)
                    .ThenInclude(v => v.ChoNgoi)
                    .Include(c => c.TuyenDuong)
                    .Include(c => c.TaiXe)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

                if (chuyenXe == null)
                {
                    TempData["Error"] = "Không tìm thấy chuyến xe";
                    return RedirectToAction(nameof(Index));
                }

                return View(chuyenXe);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Có lỗi xảy ra khi tải thông tin chuyến xe: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // Trang tổng quan về ChuyenXe (chỉ để tham khảo)
        public IActionResult Overview()
        {
            return View();
        }

        // Chuyển hướng đến trang Admin để quản lý chuyến xe
        public IActionResult Create()
        {
            TempData["Info"] = "Chức năng quản lý chuyến xe dành cho Admin. Vui lòng truy cập trang Admin.";
            return RedirectToAction("Index", "Home");
        }

        // Edit và Delete actions cũng chuyển hướng đến Admin
        public IActionResult Edit(int? id)
        {
            TempData["Info"] = "Chức năng chỉnh sửa chuyến xe dành cho Admin.";
            return RedirectToAction("Index", "Home");
        }

        public IActionResult Delete(int? id)
        {
            TempData["Info"] = "Chức năng xóa chuyến xe dành cho Admin.";
            return RedirectToAction("Index", "Home");
        }




    }











    public class NguoiDungController : Controller
    {
        private readonly DatVeXeContext _context;
        public NguoiDungController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: NguoiDung (Admin only)
        public async Task<IActionResult> Index()
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            var users = await _context.NguoiDungs
                .OrderBy(u => u.HoTen)
                .ToListAsync();
            return View(users);
        }

        // GET: NguoiDung/Details/5 (Admin only)
        public async Task<IActionResult> Details(int? id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var nguoiDung = await _context.NguoiDungs
                .FirstOrDefaultAsync(n => n.NguoiDungId == id);

            if (nguoiDung == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            // Get user's booking statistics (removed Ve references)
            ViewBag.UserTickets = new List<object>(); // Empty list since Ve is removed
            ViewBag.TotalTickets = 0;
            ViewBag.TotalSpent = 0;

            return View(nguoiDung);
        }

        // GET: NguoiDung/Create (Admin only)
        public IActionResult Create()
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            return View();
        }

        // POST: NguoiDung/Create (Admin only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("HoTen,Email,MatKhau,LaAdmin")] NguoiDung nguoiDung)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (ModelState.IsValid)
            {
                // Check if email already exists
                if (await _context.NguoiDungs.AnyAsync(u => u.Email == nguoiDung.Email))
                {
                    ModelState.AddModelError("Email", "Email đã tồn tại");
                    return View(nguoiDung);
                }

                try
                {
                    // Hash password
                    nguoiDung.MatKhau = HashPassword(nguoiDung.MatKhau);

                    _context.Add(nguoiDung);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Thêm người dùng thành công";
                    return RedirectToAction(nameof(Index));
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi thêm người dùng";
                }
            }

            return View(nguoiDung);
        }

        // GET: NguoiDung/Edit/5 (Admin only)
        public async Task<IActionResult> Edit(int? id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var nguoiDung = await _context.NguoiDungs.FindAsync(id);
            if (nguoiDung == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            // Don't show password in edit form
            nguoiDung.MatKhau = "";
            return View(nguoiDung);
        }

        // POST: NguoiDung/Edit/5 (Admin only)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("NguoiDungId,HoTen,Email,MatKhau,LaAdmin")] NguoiDung nguoiDung)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id != nguoiDung.NguoiDungId)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            // Get current user data
            var currentUser = await _context.NguoiDungs.AsNoTracking().FirstOrDefaultAsync(u => u.NguoiDungId == id);
            if (currentUser == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            // Remove password validation if not provided
            if (string.IsNullOrEmpty(nguoiDung.MatKhau))
            {
                ModelState.Remove("MatKhau");
                nguoiDung.MatKhau = currentUser.MatKhau; // Keep current password
            }
            else
            {
                nguoiDung.MatKhau = HashPassword(nguoiDung.MatKhau); // Hash new password
            }

            if (ModelState.IsValid)
            {
                // Check if email already exists (excluding current record)
                if (await _context.NguoiDungs.AnyAsync(u => u.Email == nguoiDung.Email && u.NguoiDungId != nguoiDung.NguoiDungId))
                {
                    ModelState.AddModelError("Email", "Email đã tồn tại");
                    nguoiDung.MatKhau = ""; // Clear password for security
                    return View(nguoiDung);
                }

                try
                {
                    _context.Update(nguoiDung);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật người dùng thành công";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!await NguoiDungExists(nguoiDung.NguoiDungId))
                    {
                        TempData["Error"] = "Không tìm thấy người dùng";
                        return RedirectToAction(nameof(Index));
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi cập nhật người dùng";
                }
            }

            nguoiDung.MatKhau = ""; // Clear password for security
            return View(nguoiDung);
        }

        // GET: NguoiDung/Delete/5 (Admin only)
        public async Task<IActionResult> Delete(int? id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            if (id == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            var nguoiDung = await _context.NguoiDungs
                .FirstOrDefaultAsync(n => n.NguoiDungId == id);

            if (nguoiDung == null)
            {
                TempData["Error"] = "Không tìm thấy người dùng";
                return RedirectToAction(nameof(Index));
            }

            // Check if user has bookings (removed Ve references)
            ViewBag.HasBookings = false;

            return View(nguoiDung);
        }

        // POST: NguoiDung/Delete/5 (Admin only)
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            // Check if user is admin
            if (HttpContext.Session.GetInt32("IsAdmin") != 1)
            {
                TempData["Error"] = "Bạn không có quyền truy cập chức năng này";
                return RedirectToAction("Index", "Home");
            }

            var nguoiDung = await _context.NguoiDungs.FindAsync(id);
            if (nguoiDung != null)
            {
                // Check if user has bookings
                var hasBookings = await _context.Ves.AnyAsync(v => v.NguoiDungId == id);
                if (hasBookings)
                {
                    TempData["Error"] = "Không thể xóa người dùng đã có vé đặt";
                    return RedirectToAction(nameof(Index));
                }

                try
                {
                    _context.NguoiDungs.Remove(nguoiDung);
                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Xóa người dùng thành công";
                }
                catch (Exception)
                {
                    TempData["Error"] = "Có lỗi xảy ra khi xóa người dùng";
                }
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<bool> NguoiDungExists(int id)
        {
            return await _context.NguoiDungs.AnyAsync(e => e.NguoiDungId == id);
        }

        private string HashPassword(string password)
        {
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                byte[] hashedBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
                return Convert.ToBase64String(hashedBytes);
            }
        }

        // API để lấy thông tin chuyến xe cho AJAX
        [HttpGet]
        public async Task<IActionResult> GetChuyenXeInfo(int id)
        {
            try
            {
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.Ves)
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

                if (chuyenXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy chuyến xe" });
                }

                // Add null check for Xe property
                if (chuyenXe.Xe == null)
                {
                    return Json(new { success = false, message = "Thông tin xe không có sẵn" });
                }

                var result = new
                {
                    success = true,
                    chuyenXe = new
                    {
                        diemDi = chuyenXe.DiemDiDisplay,
                        diemDen = chuyenXe.DiemDenDisplay,
                        ngayKhoiHanh = chuyenXe.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm"),
                        gia = chuyenXe.Gia.ToString("N0"),
                        bienSo = chuyenXe.Xe.BienSo,
                        loaiXe = chuyenXe.Xe.LoaiXe,
                        soGhe = chuyenXe.Xe.SoGhe,
                        soVeDaBan = chuyenXe.Ves != null ? chuyenXe.Ves.Count : 0,
                        daKhoiHanh = chuyenXe.NgayKhoiHanh <= DateTime.Now
                    }
                };

                return Json(result);
            }
            catch (Exception)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra" });
            }
        }
    }
}
