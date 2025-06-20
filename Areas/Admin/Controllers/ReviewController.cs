using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Attributes;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class ReviewController : Controller
    {
        private readonly DatVeXeContext _context;

        public ReviewController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/Review
        public async Task<IActionResult> Index(string searchString, int? diemDanhGia, bool? trangThai, 
            DateTime? tuNgay, DateTime? denNgay, int page = 1)
        {
            ViewBag.CurrentFilter = searchString;
            ViewBag.DiemDanhGiaFilter = diemDanhGia;
            ViewBag.TrangThaiFilter = trangThai;
            ViewBag.TuNgayFilter = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgayFilter = denNgay?.ToString("yyyy-MM-dd");

            var danhGias = _context.DanhGiaChuyenDis
                .Include(d => d.NguoiDung)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .AsQueryable();

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                danhGias = danhGias.Where(d => 
                    (d.NguoiDung != null && d.NguoiDung.HoTen.Contains(searchString)) ||
                    (d.NoiDung != null && d.NoiDung.Contains(searchString)) ||
                    (d.Ve != null && d.Ve.ChuyenXe != null && d.Ve.ChuyenXe.TuyenDuong != null && 
                     d.Ve.ChuyenXe.TuyenDuong.DiemDi != null && d.Ve.ChuyenXe.TuyenDuong.DiemDi.Contains(searchString)) ||
                    (d.Ve != null && d.Ve.ChuyenXe != null && d.Ve.ChuyenXe.TuyenDuong != null && 
                     d.Ve.ChuyenXe.TuyenDuong.DiemDen != null && d.Ve.ChuyenXe.TuyenDuong.DiemDen.Contains(searchString)));
            }

            // Lọc theo điểm đánh giá
            if (diemDanhGia.HasValue)
            {
                danhGias = danhGias.Where(d => d.DiemDanhGia == diemDanhGia.Value);
            }

            // Lọc theo trạng thái
            if (trangThai.HasValue)
            {
                danhGias = danhGias.Where(d => !d.BiAn == trangThai.Value);
            }

            // Lọc theo ngày
            if (tuNgay.HasValue)
            {
                danhGias = danhGias.Where(d => d.ThoiGianDanhGia.Date >= tuNgay.Value.Date);
            }
            if (denNgay.HasValue)
            {
                danhGias = danhGias.Where(d => d.ThoiGianDanhGia.Date <= denNgay.Value.Date);
            }

            // Phân trang
            int pageSize = 15;
            var totalCount = await danhGias.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            var pagedDanhGias = await danhGias
                .OrderByDescending(d => d.ThoiGianDanhGia)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pagedDanhGias);
        }

        // GET: Admin/Review/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var danhGia = await _context.DanhGiaChuyenDis
                .Include(d => d.NguoiDung)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.Xe)
                .FirstOrDefaultAsync(d => d.DanhGiaId == id);

            if (danhGia == null)
            {
                return NotFound();
            }

            return View(danhGia);
        }

        // POST: Admin/Review/ToggleStatus
        [HttpPost]
        public async Task<JsonResult> ToggleStatus(int danhGiaId)
        {
            try
            {
                var danhGia = await _context.DanhGiaChuyenDis.FindAsync(danhGiaId);
                if (danhGia == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đánh giá" });
                }

                danhGia.BiAn = !danhGia.BiAn;
                await _context.SaveChangesAsync();

                var status = danhGia.BiAn ? "ẩn" : "hiển thị";
                return Json(new {
                    success = true,
                    message = $"Đã {status} đánh giá thành công",
                    isVisible = !danhGia.BiAn
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Admin/Review/Delete
        [HttpPost]
        public async Task<JsonResult> Delete(int danhGiaId)
        {
            try
            {
                var danhGia = await _context.DanhGiaChuyenDis.FindAsync(danhGiaId);
                if (danhGia == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy đánh giá" });
                }

                _context.DanhGiaChuyenDis.Remove(danhGia);
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Xóa đánh giá thành công" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/Review/Statistics
        public async Task<IActionResult> Statistics()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Thống kê tổng quan
            var tongDanhGia = await _context.DanhGiaChuyenDis.CountAsync();
            var danhGiaThangNay = await _context.DanhGiaChuyenDis
                .CountAsync(d => d.ThoiGianDanhGia >= startOfMonth && d.ThoiGianDanhGia <= endOfMonth);
            var danhGiaHienThi = await _context.DanhGiaChuyenDis.CountAsync(d => !d.BiAn);
            var danhGiaAn = tongDanhGia - danhGiaHienThi;

            // Điểm đánh giá trung bình
            var diemTrungBinh = await _context.DanhGiaChuyenDis.AverageAsync(d => (double)d.DiemDanhGia);

            // Thống kê theo điểm đánh giá
            var thongKeTheoDiem = await _context.DanhGiaChuyenDis
                .GroupBy(d => d.DiemDanhGia)
                .Select(g => new {
                    Diem = g.Key,
                    SoLuong = g.Count(),
                    TyLe = (double)g.Count() / tongDanhGia * 100
                })
                .OrderBy(x => x.Diem)
                .ToListAsync();

            // Top tuyến đường có đánh giá cao nhất
            var topTuyenDuong = await _context.DanhGiaChuyenDis
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(d => d.Ve.ChuyenXe.TuyenDuong != null)
                .GroupBy(d => new {
                    d.Ve.ChuyenXe.TuyenDuong.DiemDi,
                    d.Ve.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    SoDanhGia = g.Count(),
                    DiemTrungBinh = g.Average(x => (double)x.DiemDanhGia)
                })
                .OrderByDescending(x => x.DiemTrungBinh)
                .Take(5)
                .ToListAsync();

            // Đánh giá mới nhất
            var danhGiaMoiNhat = await _context.DanhGiaChuyenDis
                .Include(d => d.NguoiDung)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .OrderByDescending(d => d.ThoiGianDanhGia)
                .Take(5)
                .ToListAsync();

            ViewBag.TongDanhGia = tongDanhGia;
            ViewBag.DanhGiaThangNay = danhGiaThangNay;
            ViewBag.DanhGiaHienThi = danhGiaHienThi;
            ViewBag.DanhGiaAn = danhGiaAn;
            ViewBag.DiemTrungBinh = diemTrungBinh;
            ViewBag.ThongKeTheoDiem = thongKeTheoDiem;
            ViewBag.TopTuyenDuong = topTuyenDuong;
            ViewBag.DanhGiaMoiNhat = danhGiaMoiNhat;

            return View();
        }

        // GET: Admin/Review/Export
        public async Task<IActionResult> Export(string searchString, int? diemDanhGia, bool? trangThai, 
            DateTime? tuNgay, DateTime? denNgay)
        {
            var danhGias = _context.DanhGiaChuyenDis
                .Include(d => d.NguoiDung)
                .Include(d => d.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .AsQueryable();

            // Apply same filters as Index action
            if (!string.IsNullOrEmpty(searchString))
            {
                danhGias = danhGias.Where(d => 
                    d.NguoiDung.HoTen.Contains(searchString) ||
                    d.NoiDung.Contains(searchString) ||
                    d.Ve.ChuyenXe.TuyenDuong.DiemDi.Contains(searchString) ||
                    d.Ve.ChuyenXe.TuyenDuong.DiemDen.Contains(searchString));
            }

            if (diemDanhGia.HasValue)
            {
                danhGias = danhGias.Where(d => d.DiemDanhGia == diemDanhGia.Value);
            }

            if (trangThai.HasValue)
            {
                danhGias = danhGias.Where(d => !d.BiAn == trangThai.Value);
            }

            if (tuNgay.HasValue)
            {
                danhGias = danhGias.Where(d => d.ThoiGianDanhGia.Date >= tuNgay.Value.Date);
            }
            if (denNgay.HasValue)
            {
                danhGias = danhGias.Where(d => d.ThoiGianDanhGia.Date <= denNgay.Value.Date);
            }

            var danhGiaList = await danhGias.OrderByDescending(d => d.ThoiGianDanhGia).ToListAsync();

            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Khách hàng,Email,Tuyến đường,Điểm đánh giá,Nội dung,Trạng thái,Thời gian đánh giá");
            
            foreach (var danhGia in danhGiaList)
            {
                csv.AppendLine($"\"{danhGia.NguoiDung?.HoTen}\"," +
                              $"\"{danhGia.NguoiDung?.Email}\"," +
                              $"\"{danhGia.Ve?.ChuyenXe?.TuyenDuong?.DiemDi} → {danhGia.Ve?.ChuyenXe?.TuyenDuong?.DiemDen}\"," +
                              $"{danhGia.DiemDanhGia}," +
                              $"\"{danhGia.NoiDung?.Replace("\"", "\"\"")}\"," +
                              $"\"{(danhGia.BiAn ? "Ẩn" : "Hiển thị")}\"," +
                              $"{danhGia.ThoiGianDanhGia:dd/MM/yyyy HH:mm}");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"DanhSachDanhGia_{DateTime.Now:yyyyMMdd}.csv");
        }
    }
}
