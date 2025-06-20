using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;

namespace DatVeXe.Controllers
{
    public class ReviewController : Controller
    {
        private readonly DatVeXeContext _context;

        public ReviewController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Review/Create/5
        public async Task<IActionResult> Create(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.Xe)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c!.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .FirstOrDefaultAsync(v => v.VeId == id && v.NguoiDungId == userId.Value);

            if (ve == null)
            {
                return NotFound();
            }

            // Kiểm tra vé đã hoàn thành chưa
            if (ve.VeTrangThai != TrangThaiVe.DaHoanThanh)
            {
                TempData["ErrorMessage"] = "Chỉ có thể đánh giá các chuyến đi đã hoàn thành.";
                return RedirectToAction("Details", "MyTickets", new { id });
            }

            // Kiểm tra đã đánh giá chưa
            var existingReview = await _context.DanhGiaChuyenDis
                .FirstOrDefaultAsync(d => d.VeId == id);

            if (existingReview != null)
            {
                TempData["ErrorMessage"] = "Bạn đã đánh giá chuyến đi này rồi.";
                return RedirectToAction("Details", "MyTickets", new { id });
            }

            var viewModel = new ReviewTripViewModel
            {
                VeId = id,
                Ve = ve
            };

            return View(viewModel);
        }

        // POST: Review/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(ReviewTripViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                // Reload Ve data for display
                model.Ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c!.Xe)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c!.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .FirstOrDefaultAsync(v => v.VeId == model.VeId && v.NguoiDungId == userId.Value);

                return View(model);
            }

            // Kiểm tra vé tồn tại và thuộc về user
            var ve = await _context.Ves
                .FirstOrDefaultAsync(v => v.VeId == model.VeId && v.NguoiDungId == userId.Value);

            if (ve == null)
            {
                return NotFound();
            }

            // Kiểm tra vé đã hoàn thành chưa
            if (ve.VeTrangThai != TrangThaiVe.DaHoanThanh)
            {
                TempData["ErrorMessage"] = "Chỉ có thể đánh giá các chuyến đi đã hoàn thành.";
                return RedirectToAction("Details", "MyTickets", new { id = model.VeId });
            }

            // Kiểm tra đã đánh giá chưa
            var existingReview = await _context.DanhGiaChuyenDis
                .FirstOrDefaultAsync(d => d.VeId == model.VeId);

            if (existingReview != null)
            {
                TempData["ErrorMessage"] = "Bạn đã đánh giá chuyến đi này rồi.";
                return RedirectToAction("Details", "MyTickets", new { id = model.VeId });
            }

            // Tạo đánh giá mới
            var danhGia = new DanhGiaChuyenDi
            {
                VeId = model.VeId,
                NguoiDungId = userId.Value,
                DiemDanhGia = model.DiemDanhGia,
                NoiDungDanhGia = model.NoiDungDanhGia,
                DanhGiaTaiXe = model.DanhGiaTaiXe,
                DanhGiaXe = model.DanhGiaXe,
                DanhGiaDichVu = model.DanhGiaDichVu,
                CoKhuyenNghi = model.CoKhuyenNghi,
                ThoiGianDanhGia = DateTime.Now
            };

            _context.DanhGiaChuyenDis.Add(danhGia);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Cảm ơn bạn đã đánh giá chuyến đi! Đánh giá của bạn sẽ giúp chúng tôi cải thiện dịch vụ.";
            return RedirectToAction("Details", "MyTickets", new { id = model.VeId });
        }

        // GET: Review/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var danhGia = await _context.DanhGiaChuyenDis
                .Include(d => d.Ve)
                    .ThenInclude(v => v!.ChuyenXe)
                        .ThenInclude(c => c!.Xe)
                .Include(d => d.Ve)
                    .ThenInclude(v => v!.ChuyenXe)
                        .ThenInclude(c => c!.TuyenDuong)
                .Include(d => d.Ve)
                    .ThenInclude(v => v!.ChoNgoi)
                .FirstOrDefaultAsync(d => d.DanhGiaId == id && d.NguoiDungId == userId.Value);

            if (danhGia == null)
            {
                return NotFound();
            }

            var viewModel = new ReviewTripViewModel
            {
                VeId = danhGia.VeId,
                Ve = danhGia.Ve!,
                DiemDanhGia = danhGia.DiemDanhGia,
                NoiDungDanhGia = danhGia.NoiDungDanhGia,
                DanhGiaTaiXe = danhGia.DanhGiaTaiXe,
                DanhGiaXe = danhGia.DanhGiaXe,
                DanhGiaDichVu = danhGia.DanhGiaDichVu,
                CoKhuyenNghi = danhGia.CoKhuyenNghi
            };

            return View(viewModel);
        }

        // POST: Review/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, ReviewTripViewModel model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (!ModelState.IsValid)
            {
                // Reload Ve data for display
                model.Ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c!.Xe)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(c => c!.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .FirstOrDefaultAsync(v => v.VeId == model.VeId);

                return View(model);
            }

            var danhGia = await _context.DanhGiaChuyenDis
                .FirstOrDefaultAsync(d => d.DanhGiaId == id && d.NguoiDungId == userId.Value);

            if (danhGia == null)
            {
                return NotFound();
            }

            // Cập nhật đánh giá
            danhGia.DiemDanhGia = model.DiemDanhGia;
            danhGia.NoiDungDanhGia = model.NoiDungDanhGia;
            danhGia.DanhGiaTaiXe = model.DanhGiaTaiXe;
            danhGia.DanhGiaXe = model.DanhGiaXe;
            danhGia.DanhGiaDichVu = model.DanhGiaDichVu;
            danhGia.CoKhuyenNghi = model.CoKhuyenNghi;
            danhGia.ThoiGianDanhGia = DateTime.Now; // Cập nhật thời gian chỉnh sửa

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Đã cập nhật đánh giá thành công!";
            return RedirectToAction("Details", "MyTickets", new { id = model.VeId });
        }

        // GET: Review/List - Hiển thị tất cả đánh giá của một chuyến xe
        public async Task<IActionResult> List(int chuyenXeId, int page = 1, int pageSize = 10)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == chuyenXeId);

            if (chuyenXe == null)
            {
                return NotFound();
            }

            var query = _context.DanhGiaChuyenDis
                .Include(d => d.Ve)
                .Include(d => d.NguoiDung)
                .Where(d => d.Ve!.ChuyenXeId == chuyenXeId)
                .OrderByDescending(d => d.ThoiGianDanhGia);

            var totalReviews = await query.CountAsync();
            var reviews = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            ViewBag.ChuyenXe = chuyenXe;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalReviews / pageSize);
            ViewBag.TotalReviews = totalReviews;

            if (totalReviews > 0)
            {
                ViewBag.AverageRating = await query.AverageAsync(d => d.DiemDanhGia);
            }

            return View(reviews);
        }
    }
}
