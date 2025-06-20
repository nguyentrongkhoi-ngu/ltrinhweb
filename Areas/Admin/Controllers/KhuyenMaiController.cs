using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Attributes;
using System.Text;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class KhuyenMaiController : Controller
    {
        private readonly DatVeXeContext _context;

        public KhuyenMaiController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/KhuyenMai/TongQuan
        public async Task<IActionResult> TongQuan()
        {
            var now = DateTime.Now;
            var today = now.Date;

            // Thống kê tổng quan
            var tongKhuyenMai = await _context.KhuyenMais.CountAsync();
            var khuyenMaiHoatDong = await _context.KhuyenMais.CountAsync(k => k.TrangThaiHoatDong);

            // Thống kê sử dụng hôm nay
            var tongLuotSuDung = await _context.LichSuKhuyenMais
                .CountAsync(l => l.ThoiGianSuDung.Date == today);
            var tongTienTietKiem = await _context.LichSuKhuyenMais
                .Where(l => l.ThoiGianSuDung.Date == today)
                .SumAsync(l => l.GiaTriGiam);

            // Khuyến mãi sắp hết hạn
            var khuyenMaiSapHetHan = await _context.KhuyenMais
                .Where(k => k.TrangThaiHoatDong && k.NgayKetThuc >= now && k.NgayKetThuc <= now.AddDays(7))
                .OrderBy(k => k.NgayKetThuc)
                .ToListAsync();

            ViewBag.TongKhuyenMai = tongKhuyenMai;
            ViewBag.KhuyenMaiHoatDong = khuyenMaiHoatDong;
            ViewBag.TongLuotSuDung = tongLuotSuDung;
            ViewBag.TongTienTietKiem = tongTienTietKiem;
            ViewBag.KhuyenMaiSapHetHan = khuyenMaiSapHetHan;

            return View();
        }

        // GET: Admin/KhuyenMai
        public async Task<IActionResult> Index()
        {
            var khuyenMais = await _context.KhuyenMais
                .OrderByDescending(k => k.NgayTao)
                .ToListAsync();

            return View(khuyenMais);
        }

        // GET: Admin/KhuyenMai/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var khuyenMai = await _context.KhuyenMais
                .FirstOrDefaultAsync(m => m.KhuyenMaiId == id);
            
            if (khuyenMai == null)
            {
                return NotFound();
            }

            return View(khuyenMai);
        }

        // GET: Admin/KhuyenMai/Create
        public IActionResult Create()
        {
            try
            {
                ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
                ViewBag.TestMessage = "Controller hoạt động bình thường";

                // Tạo model mới với giá trị mặc định
                var model = new KhuyenMai
                {
                    NgayBatDau = DateTime.Now,
                    NgayKetThuc = DateTime.Now.AddMonths(1),
                    TrangThaiHoatDong = true,
                    SoLanSuDungToiDa = 1
                };

                return View(model);
            }
            catch (Exception ex)
            {
                // Ghi log lỗi nếu có
                ViewBag.Error = "Lỗi khi load trang tạo khuyến mãi: " + ex.Message;
                ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
                return View(new KhuyenMai());
            }
        }

        // Test action để debug
        public IActionResult Test()
        {
            ViewBag.Message = "Test page hoạt động!";
            ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
            return View("Create", new KhuyenMai
            {
                NgayBatDau = DateTime.Now,
                NgayKetThuc = DateTime.Now.AddMonths(1),
                TrangThaiHoatDong = true,
                SoLanSuDungToiDa = 1
            });
        }

        // Simple Create action
        public IActionResult CreateSimple()
        {
            ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
            ViewBag.TestMessage = "Trang tạo khuyến mãi đơn giản";

            var model = new KhuyenMai
            {
                NgayBatDau = DateTime.Now,
                NgayKetThuc = DateTime.Now.AddMonths(1),
                TrangThaiHoatDong = true,
                SoLanSuDungToiDa = 1
            };

            return View(model);
        }

        // Test Create action
        public IActionResult CreateTest()
        {
            ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
            ViewBag.TestMessage = "Trang test hoạt động bình thường!";

            var model = new KhuyenMai
            {
                NgayBatDau = DateTime.Now,
                NgayKetThuc = DateTime.Now.AddMonths(1),
                TrangThaiHoatDong = true,
                SoLanSuDungToiDa = 1
            };

            return View(model);
        }

        // POST: Admin/KhuyenMai/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("TenKhuyenMai,MaKhuyenMai,MoTa,LoaiKhuyenMai,GiaTri,GiaTriToiDa,GiaTriDonHangToiThieu,NgayBatDau,NgayKetThuc,SoLuongToiDa,SoLanSuDungToiDa,TrangThaiHoatDong")] KhuyenMai khuyenMai)
        {
            try
            {
                if (ModelState.IsValid)
                {
                    // Kiểm tra mã khuyến mãi đã tồn tại chưa
                    if (await _context.KhuyenMais.AnyAsync(k => k.MaKhuyenMai == khuyenMai.MaKhuyenMai))
                    {
                        ModelState.AddModelError("MaKhuyenMai", "Mã khuyến mãi đã tồn tại");
                        ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
                        return View(khuyenMai);
                    }

                    // Validation bổ sung
                    if (khuyenMai.NgayBatDau >= khuyenMai.NgayKetThuc)
                    {
                        ModelState.AddModelError("NgayKetThuc", "Ngày kết thúc phải sau ngày bắt đầu");
                        ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
                        return View(khuyenMai);
                    }

                    if (khuyenMai.LoaiKhuyenMai == LoaiKhuyenMai.GiamPhanTram && khuyenMai.GiaTri > 100)
                    {
                        ModelState.AddModelError("GiaTri", "Phần trăm giảm không được vượt quá 100%");
                        ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
                        return View(khuyenMai);
                    }

                    khuyenMai.NgayTao = DateTime.Now;
                    khuyenMai.SoLuongDaSuDung = 0;
                    khuyenMai.MaKhuyenMai = khuyenMai.MaKhuyenMai.ToUpper(); // Đảm bảo mã viết hoa

                    _context.Add(khuyenMai);
                    await _context.SaveChangesAsync();

                    TempData["Success"] = "Tạo khuyến mãi thành công!";
                    return RedirectToAction(nameof(Index));
                }
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
            }

            // Trả về view với ViewBag
            ViewBag.LoaiKhuyenMaiList = Enum.GetValues(typeof(LoaiKhuyenMai));
            return View(khuyenMai);
        }

        // GET: Admin/KhuyenMai/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var khuyenMai = await _context.KhuyenMais.FindAsync(id);
            if (khuyenMai == null)
            {
                return NotFound();
            }
            return View(khuyenMai);
        }

        // POST: Admin/KhuyenMai/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("KhuyenMaiId,MaKhuyenMai,TenKhuyenMai,MoTa,LoaiKhuyenMai,GiaTri,GiaTriDonHangToiThieu,GiaTriToiDa,NgayBatDau,NgayKetThuc,SoLuongToiDa,SoLanSuDungToiDa,TrangThaiHoatDong")] KhuyenMai khuyenMai)
        {
            if (id != khuyenMai.KhuyenMaiId)
            {
                return NotFound();
            }

            if (ModelState.IsValid)
            {
                try
                {
                    // Kiểm tra mã khuyến mãi trùng lặp
                    var existingCode = await _context.KhuyenMais
                        .AnyAsync(k => k.MaKhuyenMai == khuyenMai.MaKhuyenMai && k.KhuyenMaiId != khuyenMai.KhuyenMaiId);
                    
                    if (existingCode)
                    {
                        ModelState.AddModelError("MaKhuyenMai", "Mã khuyến mãi đã tồn tại");
                        return View(khuyenMai);
                    }

                    khuyenMai.NgayCapNhat = DateTime.Now;
                    _context.Update(khuyenMai);
                    await _context.SaveChangesAsync();
                    
                    TempData["SuccessMessage"] = "Cập nhật khuyến mãi thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!KhuyenMaiExists(khuyenMai.KhuyenMaiId))
                    {
                        return NotFound();
                    }
                    else
                    {
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "Có lỗi xảy ra: " + ex.Message);
                }
            }
            return View(khuyenMai);
        }

        // GET: Admin/KhuyenMai/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var khuyenMai = await _context.KhuyenMais
                .FirstOrDefaultAsync(m => m.KhuyenMaiId == id);
            if (khuyenMai == null)
            {
                return NotFound();
            }

            return View(khuyenMai);
        }

        // POST: Admin/KhuyenMai/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            try
            {
                var khuyenMai = await _context.KhuyenMais.FindAsync(id);
                if (khuyenMai != null)
                {
                    _context.KhuyenMais.Remove(khuyenMai);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = "Xóa khuyến mãi thành công!";
                }
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = "Có lỗi xảy ra: " + ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        // Bật/tắt khuyến mãi
        [HttpPost]
        public async Task<JsonResult> ToggleStatus(int id, bool status)
        {
            try
            {
                var khuyenMai = await _context.KhuyenMais.FindAsync(id);
                if (khuyenMai == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy khuyến mãi" });
                }

                khuyenMai.TrangThaiHoatDong = status;
                khuyenMai.NgayCapNhat = DateTime.Now;
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = status ? "Đã kích hoạt khuyến mãi" : "Đã tạm dừng khuyến mãi" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Kiểm tra mã khuyến mãi
        [HttpPost]
        public async Task<JsonResult> CheckPromoCode(string code)
        {
            try
            {
                var khuyenMai = await _context.KhuyenMais
                    .FirstOrDefaultAsync(k => k.MaKhuyenMai == code && 
                                            k.TrangThaiHoatDong && 
                                            k.NgayBatDau <= DateTime.Now && 
                                            k.NgayKetThuc >= DateTime.Now);

                if (khuyenMai == null)
                {
                    return Json(new { success = false, message = "Mã khuyến mãi không hợp lệ hoặc đã hết hạn" });
                }

                // Kiểm tra số lượng đã sử dụng
                if (khuyenMai.SoLuongToiDa.HasValue && khuyenMai.SoLuongDaSuDung >= khuyenMai.SoLuongToiDa)
                {
                    return Json(new { success = false, message = "Mã khuyến mãi đã hết lượt sử dụng" });
                }

                return Json(new { 
                    success = true, 
                    khuyenMai = new {
                        khuyenMai.KhuyenMaiId,
                        khuyenMai.TenKhuyenMai,
                        khuyenMai.LoaiKhuyenMai,
                        khuyenMai.GiaTri,
                        khuyenMai.GiaTriToiDa,
                        khuyenMai.GiaTriDonHangToiThieu
                    }
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // Dashboard thống kê khuyến mãi
        public async Task<IActionResult> Dashboard()
        {
            var now = DateTime.Now;
            var startOfMonth = new DateTime(now.Year, now.Month, 1);
            var endOfMonth = startOfMonth.AddMonths(1).AddDays(-1);

            // Thống kê tổng quan
            var tongKhuyenMai = await _context.KhuyenMais.CountAsync();
            var khuyenMaiHoatDong = await _context.KhuyenMais.CountAsync(k => k.TrangThaiHoatDong);
            var khuyenMaiHetHan = await _context.KhuyenMais.CountAsync(k => k.NgayKetThuc < now);
            var khuyenMaiSapHetHan = await _context.KhuyenMais
                .CountAsync(k => k.NgayKetThuc >= now && k.NgayKetThuc <= now.AddDays(7) && k.TrangThaiHoatDong);

            // Thống kê sử dụng trong tháng
            var lichSuThang = await _context.LichSuKhuyenMais
                .Include(l => l.KhuyenMai)
                .Where(l => l.ThoiGianSuDung >= startOfMonth && l.ThoiGianSuDung <= endOfMonth)
                .ToListAsync();

            var tongLuotSuDung = lichSuThang.Count;
            var tongTienTietKiem = lichSuThang.Sum(l => l.GiaTriGiam);

            // Top 5 khuyến mãi được sử dụng nhiều nhất
            var topKhuyenMai = await _context.LichSuKhuyenMais
                .Include(l => l.KhuyenMai)
                .Where(l => l.ThoiGianSuDung >= startOfMonth && l.ThoiGianSuDung <= endOfMonth)
                .GroupBy(l => new { l.KhuyenMaiId, l.KhuyenMai.TenKhuyenMai, l.KhuyenMai.MaKhuyenMai })
                .Select(g => new
                {
                    KhuyenMaiId = g.Key.KhuyenMaiId,
                    TenKhuyenMai = g.Key.TenKhuyenMai,
                    MaKhuyenMai = g.Key.MaKhuyenMai,
                    SoLuotSuDung = g.Count(),
                    TongTienGiam = g.Sum(x => x.GiaTriGiam)
                })
                .OrderByDescending(x => x.SoLuotSuDung)
                .Take(5)
                .ToListAsync();

            // Thống kê theo ngày trong tháng (cho biểu đồ)
            var thongKeTheoNgay = await _context.LichSuKhuyenMais
                .Where(l => l.ThoiGianSuDung >= startOfMonth && l.ThoiGianSuDung <= endOfMonth)
                .GroupBy(l => l.ThoiGianSuDung.Date)
                .Select(g => new
                {
                    Ngay = g.Key,
                    SoLuotSuDung = g.Count(),
                    TongTienGiam = g.Sum(x => x.GiaTriGiam)
                })
                .OrderBy(x => x.Ngay)
                .ToListAsync();

            ViewBag.TongKhuyenMai = tongKhuyenMai;
            ViewBag.KhuyenMaiHoatDong = khuyenMaiHoatDong;
            ViewBag.KhuyenMaiHetHan = khuyenMaiHetHan;
            ViewBag.KhuyenMaiSapHetHan = khuyenMaiSapHetHan;
            ViewBag.TongLuotSuDung = tongLuotSuDung;
            ViewBag.TongTienTietKiem = tongTienTietKiem;
            ViewBag.TopKhuyenMai = topKhuyenMai;
            ViewBag.ThongKeTheoNgay = thongKeTheoNgay;

            return View();
        }

        // Quản lý lịch sử sử dụng khuyến mãi
        public async Task<IActionResult> LichSuSuDung(int? khuyenMaiId, DateTime? tuNgay, DateTime? denNgay, int page = 1)
        {
            var pageSize = 20;
            var query = _context.LichSuKhuyenMais
                .Include(l => l.KhuyenMai)
                .Include(l => l.NguoiDung)
                .Include(l => l.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                .AsQueryable();

            // Lọc theo khuyến mãi
            if (khuyenMaiId.HasValue)
            {
                query = query.Where(l => l.KhuyenMaiId == khuyenMaiId.Value);
            }

            // Lọc theo thời gian
            if (tuNgay.HasValue)
            {
                query = query.Where(l => l.ThoiGianSuDung.Date >= tuNgay.Value.Date);
            }

            if (denNgay.HasValue)
            {
                query = query.Where(l => l.ThoiGianSuDung.Date <= denNgay.Value.Date);
            }

            var totalItems = await query.CountAsync();
            var lichSu = await query
                .OrderByDescending(l => l.ThoiGianSuDung)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Danh sách khuyến mãi cho dropdown
            var khuyenMais = await _context.KhuyenMais
                .OrderBy(k => k.TenKhuyenMai)
                .ToListAsync();

            ViewBag.KhuyenMais = khuyenMais;
            ViewBag.CurrentKhuyenMaiId = khuyenMaiId;
            ViewBag.CurrentTuNgay = tuNgay;
            ViewBag.CurrentDenNgay = denNgay;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            ViewBag.TotalItems = totalItems;

            return View(lichSu);
        }

        // Copy khuyến mãi
        public async Task<IActionResult> Copy(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var khuyenMai = await _context.KhuyenMais.FindAsync(id);
            if (khuyenMai == null)
            {
                return NotFound();
            }

            // Tạo bản copy
            var khuyenMaiCopy = new KhuyenMai
            {
                TenKhuyenMai = khuyenMai.TenKhuyenMai + " (Copy)",
                MaKhuyenMai = "", // Để trống để user nhập mã mới
                MoTa = khuyenMai.MoTa,
                LoaiKhuyenMai = khuyenMai.LoaiKhuyenMai,
                GiaTri = khuyenMai.GiaTri,
                GiaTriToiDa = khuyenMai.GiaTriToiDa,
                GiaTriDonHangToiThieu = khuyenMai.GiaTriDonHangToiThieu,
                NgayBatDau = DateTime.Now.Date,
                NgayKetThuc = DateTime.Now.Date.AddDays(30),
                SoLuongToiDa = khuyenMai.SoLuongToiDa,
                SoLanSuDungToiDa = khuyenMai.SoLanSuDungToiDa,
                TrangThaiHoatDong = false // Mặc định tắt
            };

            return View("Create", khuyenMaiCopy);
        }

        // Báo cáo chi tiết khuyến mãi
        public async Task<IActionResult> BaoCao(int? id, DateTime? tuNgay, DateTime? denNgay)
        {
            if (id == null)
            {
                return NotFound();
            }

            var khuyenMai = await _context.KhuyenMais.FindAsync(id);
            if (khuyenMai == null)
            {
                return NotFound();
            }

            var query = _context.LichSuKhuyenMais
                .Include(l => l.NguoiDung)
                .Include(l => l.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(l => l.KhuyenMaiId == id);

            if (tuNgay.HasValue)
            {
                query = query.Where(l => l.ThoiGianSuDung.Date >= tuNgay.Value.Date);
            }

            if (denNgay.HasValue)
            {
                query = query.Where(l => l.ThoiGianSuDung.Date <= denNgay.Value.Date);
            }

            var lichSuSuDung = await query
                .OrderByDescending(l => l.ThoiGianSuDung)
                .ToListAsync();

            // Thống kê tổng quan
            var tongLuotSuDung = lichSuSuDung.Count;
            var tongTienGiam = lichSuSuDung.Sum(l => l.GiaTriGiam);
            var trungBinhGiamMoiLan = tongLuotSuDung > 0 ? tongTienGiam / tongLuotSuDung : 0;

            // Thống kê theo tuyến đường
            var thongKeTuyen = lichSuSuDung
                .Where(l => l.Ve?.ChuyenXe?.TuyenDuong != null)
                .GroupBy(l => new {
                    l.Ve.ChuyenXe.TuyenDuong.DiemDi,
                    l.Ve.ChuyenXe.TuyenDuong.DiemDen
                })
                .Select(g => new
                {
                    TuyenDuong = $"{g.Key.DiemDi} → {g.Key.DiemDen}",
                    SoLuotSuDung = g.Count(),
                    TongTienGiam = g.Sum(x => x.GiaTriGiam)
                })
                .OrderByDescending(x => x.SoLuotSuDung)
                .ToList();

            ViewBag.KhuyenMai = khuyenMai;
            ViewBag.TongLuotSuDung = tongLuotSuDung;
            ViewBag.TongTienGiam = tongTienGiam;
            ViewBag.TrungBinhGiamMoiLan = trungBinhGiamMoiLan;
            ViewBag.ThongKeTuyen = thongKeTuyen;
            ViewBag.TuNgay = tuNgay;
            ViewBag.DenNgay = denNgay;

            return View(lichSuSuDung);
        }

        // Xuất báo cáo Excel
        [HttpPost]
        public async Task<IActionResult> XuatBaoCao(int? khuyenMaiId, DateTime? tuNgay, DateTime? denNgay, string loaiBaoCao = "lichsu")
        {
            try
            {
                var query = _context.LichSuKhuyenMais
                    .Include(l => l.KhuyenMai)
                    .Include(l => l.NguoiDung)
                    .Include(l => l.Ve)
                        .ThenInclude(v => v.ChuyenXe)
                            .ThenInclude(c => c.TuyenDuong)
                    .AsQueryable();

                if (khuyenMaiId.HasValue)
                {
                    query = query.Where(l => l.KhuyenMaiId == khuyenMaiId.Value);
                }

                if (tuNgay.HasValue)
                {
                    query = query.Where(l => l.ThoiGianSuDung.Date >= tuNgay.Value.Date);
                }

                if (denNgay.HasValue)
                {
                    query = query.Where(l => l.ThoiGianSuDung.Date <= denNgay.Value.Date);
                }

                var data = await query.OrderByDescending(l => l.ThoiGianSuDung).ToListAsync();

                // Tạo CSV content
                var csv = new StringBuilder();
                csv.AppendLine("STT,Mã khuyến mãi,Tên khuyến mãi,Khách hàng,Số điện thoại,Tuyến đường,Thời gian sử dụng,Giá trị giảm");

                for (int i = 0; i < data.Count; i++)
                {
                    var item = data[i];
                    var tuyenDuong = item.Ve?.ChuyenXe?.TuyenDuong != null
                        ? $"{item.Ve.ChuyenXe.TuyenDuong.DiemDi} → {item.Ve.ChuyenXe.TuyenDuong.DiemDen}"
                        : "N/A";

                    csv.AppendLine($"{i + 1},{item.MaKhuyenMai},{item.KhuyenMai?.TenKhuyenMai},{item.NguoiDung?.HoTen},{item.NguoiDung?.SoDienThoai},{tuyenDuong},{item.ThoiGianSuDung:dd/MM/yyyy HH:mm},{item.GiaTriGiam:N0}");
                }

                var fileName = $"BaoCaoKhuyenMai_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
                var bytes = Encoding.UTF8.GetBytes(csv.ToString());

                return File(bytes, "text/csv", fileName);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Có lỗi xảy ra khi xuất báo cáo: " + ex.Message;
                return RedirectToAction(nameof(LichSuSuDung));
            }
        }

        // Tìm kiếm nâng cao
        public async Task<IActionResult> TimKiemNangCao(string tenKhuyenMai, string maKhuyenMai,
            LoaiKhuyenMai? loaiKhuyenMai, bool? trangThaiHoatDong, DateTime? tuNgay, DateTime? denNgay,
            decimal? giaTriTu, decimal? giaTriDen, int page = 1)
        {
            var pageSize = 10;
            var query = _context.KhuyenMais.AsQueryable();

            // Áp dụng các bộ lọc
            if (!string.IsNullOrEmpty(tenKhuyenMai))
            {
                query = query.Where(k => k.TenKhuyenMai.Contains(tenKhuyenMai));
            }

            if (!string.IsNullOrEmpty(maKhuyenMai))
            {
                query = query.Where(k => k.MaKhuyenMai.Contains(maKhuyenMai));
            }

            if (loaiKhuyenMai.HasValue)
            {
                query = query.Where(k => k.LoaiKhuyenMai == loaiKhuyenMai.Value);
            }

            if (trangThaiHoatDong.HasValue)
            {
                query = query.Where(k => k.TrangThaiHoatDong == trangThaiHoatDong.Value);
            }

            if (tuNgay.HasValue)
            {
                query = query.Where(k => k.NgayBatDau >= tuNgay.Value.Date);
            }

            if (denNgay.HasValue)
            {
                query = query.Where(k => k.NgayKetThuc <= denNgay.Value.Date);
            }

            if (giaTriTu.HasValue)
            {
                query = query.Where(k => k.GiaTri >= giaTriTu.Value);
            }

            if (giaTriDen.HasValue)
            {
                query = query.Where(k => k.GiaTri <= giaTriDen.Value);
            }

            var totalItems = await query.CountAsync();
            var khuyenMais = await query
                .OrderByDescending(k => k.NgayTao)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Lưu các giá trị tìm kiếm để hiển thị lại
            ViewBag.CurrentTenKhuyenMai = tenKhuyenMai;
            ViewBag.CurrentMaKhuyenMai = maKhuyenMai;
            ViewBag.CurrentLoaiKhuyenMai = loaiKhuyenMai;
            ViewBag.CurrentTrangThaiHoatDong = trangThaiHoatDong;
            ViewBag.CurrentTuNgay = tuNgay;
            ViewBag.CurrentDenNgay = denNgay;
            ViewBag.CurrentGiaTriTu = giaTriTu;
            ViewBag.CurrentGiaTriDen = giaTriDen;
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = (int)Math.Ceiling((double)totalItems / pageSize);
            ViewBag.TotalItems = totalItems;

            return View(khuyenMais);
        }

        private bool KhuyenMaiExists(int id)
        {
            return _context.KhuyenMais.Any(e => e.KhuyenMaiId == id);
        }
    }
}
