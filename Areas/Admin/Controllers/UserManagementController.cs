using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Attributes;
using System.Security.Cryptography;
using System.Text;
using DatVeXe.Extensions;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [AdminAuthorization]
    public class UserManagementController : Controller
    {
        private readonly DatVeXeContext _context;

        public UserManagementController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/UserManagement
        public async Task<IActionResult> Index(string searchString, string sortOrder, bool? isActive, bool? isAdmin, int page = 1)
        {
            ViewBag.CurrentSort = sortOrder;
            ViewBag.NameSortParm = string.IsNullOrEmpty(sortOrder) ? "name_desc" : "";
            ViewBag.DateSortParm = sortOrder == "date" ? "date_desc" : "date";
            ViewBag.EmailSortParm = sortOrder == "email" ? "email_desc" : "email";
            ViewBag.CurrentFilter = searchString;
            ViewBag.IsActiveFilter = isActive?.ToString().ToLower();
            ViewBag.IsAdminFilter = isAdmin?.ToString().ToLower();

            var users = from u in _context.NguoiDungs select u;

            // Tìm kiếm
            if (!string.IsNullOrEmpty(searchString))
            {
                users = users.Where(u => u.HoTen.Contains(searchString) || 
                                       u.Email.Contains(searchString) ||
                                       (u.SoDienThoai != null && u.SoDienThoai.Contains(searchString)));
            }

            // Lọc theo trạng thái hoạt động
            if (isActive.HasValue)
            {
                users = users.Where(u => u.TrangThaiHoatDong == isActive.Value);
            }

            // Lọc theo quyền admin
            if (isAdmin.HasValue)
            {
                users = users.Where(u => u.LaAdmin == isAdmin.Value);
            }

            // Sắp xếp
            switch (sortOrder)
            {
                case "name_desc":
                    users = users.OrderByDescending(u => u.HoTen);
                    break;
                case "date":
                    users = users.OrderBy(u => u.NgayDangKy);
                    break;
                case "date_desc":
                    users = users.OrderByDescending(u => u.NgayDangKy);
                    break;
                case "email":
                    users = users.OrderBy(u => u.Email);
                    break;
                case "email_desc":
                    users = users.OrderByDescending(u => u.Email);
                    break;
                default:
                    users = users.OrderBy(u => u.HoTen);
                    break;
            }

            // Phân trang
            int pageSize = 10;
            var totalCount = await users.CountAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
            
            ViewBag.CurrentPage = page;
            ViewBag.TotalPages = totalPages;
            ViewBag.TotalCount = totalCount;

            var pagedUsers = await users
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return View(pagedUsers);
        }

        // GET: Admin/UserManagement/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.NguoiDungs
                .Include(u => u.Ves)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .FirstOrDefaultAsync(u => u.NguoiDungId == id);

            if (user == null)
            {
                return NotFound();
            }

            // Thống kê người dùng
            ViewBag.TotalTickets = user.Ves?.Count ?? 0;            ViewBag.TotalSpent = await _context.ThanhToans
                .Where(t => t.Ve != null && t.Ve.NguoiDungId == id && t.TrangThai == TrangThaiThanhToan.ThanhCong)
                .SumAsync(t => t.SoTien);            ViewBag.CompletedTrips = user.Ves?.Count(v => v.VeTrangThai == TrangThaiVe.DaHoanThanh) ?? 0;
            ViewBag.CancelledTickets = user.Ves?.Count(v => v.VeTrangThai == TrangThaiVe.DaHuy) ?? 0;

            return View(user);
        }

        // GET: Admin/UserManagement/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/UserManagement/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("HoTen,Email,MatKhau,SoDienThoai,DiaChi,NgaySinh,GioiTinh,LaAdmin,TrangThaiHoatDong")] NguoiDung nguoiDung)
        {
            if (ModelState.IsValid)
            {
                // Kiểm tra email đã tồn tại
                if (await _context.NguoiDungs.AnyAsync(u => u.Email == nguoiDung.Email))
                {
                    ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống");
                    return View(nguoiDung);
                }

                // Mã hóa mật khẩu
                nguoiDung.MatKhau = HashPassword(nguoiDung.MatKhau);
                nguoiDung.NgayDangKy = DateTime.Now;
                nguoiDung.TrangThaiHoatDong = true;

                _context.Add(nguoiDung);
                await _context.SaveChangesAsync();
                
                TempData["Success"] = "Tạo người dùng thành công!";
                return RedirectToAction(nameof(Index));
            }
            return View(nguoiDung);
        }

        // GET: Admin/UserManagement/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null)
            {
                return NotFound();
            }

            var user = await _context.NguoiDungs.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            // Không hiển thị mật khẩu
            user.MatKhau = "";
            return View(user);
        }

        // POST: Admin/UserManagement/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("NguoiDungId,HoTen,Email,SoDienThoai,DiaChi,NgaySinh,GioiTinh,LaAdmin,TrangThaiHoatDong,TaiKhoanBiKhoa,LyDoKhoa,NgayDangKy")] NguoiDung nguoiDung, string? newPassword)
        {
            if (id != nguoiDung.NguoiDungId)
            {
                return NotFound();
            }

            // Remove validation for MatKhau since it's not being updated directly
            ModelState.Remove("MatKhau");

            if (ModelState.IsValid)
            {
                try
                {
                    var existingUser = await _context.NguoiDungs.FindAsync(id);
                    if (existingUser == null)
                    {
                        return NotFound();
                    }

                    // Kiểm tra email trùng lặp (trừ user hiện tại)
                    if (await _context.NguoiDungs.AnyAsync(u => u.Email == nguoiDung.Email && u.NguoiDungId != id))
                    {
                        ModelState.AddModelError("Email", "Email đã tồn tại trong hệ thống");
                        nguoiDung.MatKhau = "";
                        return View(nguoiDung);
                    }

                    // Cập nhật thông tin
                    existingUser.HoTen = nguoiDung.HoTen;
                    existingUser.Email = nguoiDung.Email;
                    existingUser.SoDienThoai = nguoiDung.SoDienThoai;
                    existingUser.DiaChi = nguoiDung.DiaChi;
                    existingUser.NgaySinh = nguoiDung.NgaySinh;
                    existingUser.GioiTinh = nguoiDung.GioiTinh;
                    existingUser.LaAdmin = nguoiDung.LaAdmin;
                    existingUser.TrangThaiHoatDong = nguoiDung.TrangThaiHoatDong;
                    existingUser.TaiKhoanBiKhoa = nguoiDung.TaiKhoanBiKhoa;
                    existingUser.LyDoKhoa = nguoiDung.LyDoKhoa;
                    // NgayCapNhat is NotMapped, so we don't update it

                    // Nếu có mật khẩu mới
                    if (!string.IsNullOrEmpty(newPassword))
                    {
                        if (newPassword.Length < 6)
                        {
                            ModelState.AddModelError("", "Mật khẩu mới phải có ít nhất 6 ký tự");
                            nguoiDung.MatKhau = "";
                            return View(nguoiDung);
                        }
                        existingUser.MatKhau = HashPassword(newPassword);
                    }

                    await _context.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật thông tin người dùng thành công!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!UserExists(nguoiDung.NguoiDungId))
                    {
                        return NotFound();
                    }
                    throw;
                }
            }
            nguoiDung.MatKhau = "";
            return View(nguoiDung);
        }

        // POST: Admin/UserManagement/ToggleStatus/5
        [HttpPost]
        public async Task<JsonResult> ToggleStatus(int userId)
        {
            try
            {
                var user = await _context.NguoiDungs.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy người dùng" });
                }

                user.TrangThaiHoatDong = !user.TrangThaiHoatDong;
                // NgayCapNhat is NotMapped, so we don't update it
                await _context.SaveChangesAsync();

                var status = user.TrangThaiHoatDong ? "kích hoạt" : "vô hiệu hóa";
                return Json(new { 
                    success = true, 
                    message = $"Đã {status} tài khoản thành công",
                    isActive = user.TrangThaiHoatDong
                });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // POST: Admin/UserManagement/ToggleLock/5
        [HttpPost]
        public async Task<JsonResult> ToggleLock(int userId, string? reason)
        {
            try
            {
                var user = await _context.NguoiDungs.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy người dùng" });
                }

                user.TaiKhoanBiKhoa = !user.TaiKhoanBiKhoa;
                // NgayCapNhat is NotMapped, so we don't update it
                
                if (user.TaiKhoanBiKhoa && !string.IsNullOrEmpty(reason))
                {
                    user.LyDoKhoa = reason;
                }
                else if (!user.TaiKhoanBiKhoa)
                {
                    user.LyDoKhoa = null;
                }

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

        // POST: Admin/UserManagement/ResetPassword/5
        [HttpPost]
        public async Task<JsonResult> ResetPassword(int userId, string newPassword)
        {
            try
            {
                if (string.IsNullOrEmpty(newPassword) || newPassword.Length < 6)
                {
                    return Json(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự" });
                }

                var user = await _context.NguoiDungs.FindAsync(userId);
                if (user == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy người dùng" });
                }

                user.MatKhau = HashPassword(newPassword);
                // NgayCapNhat is NotMapped, so we don't update it
                await _context.SaveChangesAsync();

                return Json(new { success = true, message = "Đặt lại mật khẩu thành công" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = "Có lỗi xảy ra: " + ex.Message });
            }
        }

        // GET: Admin/UserManagement/UserStatistics/5
        public async Task<IActionResult> UserStatistics(int id)
        {
            var user = await _context.NguoiDungs
                .Include(u => u.Ves)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .FirstOrDefaultAsync(u => u.NguoiDungId == id);

            if (user == null)
            {
                return NotFound();
            }            // Lịch sử giao dịch
            var transactions = await _context.ThanhToans
                .Include(t => t.Ve)
                    .ThenInclude(v => v.ChuyenXe)
                        .ThenInclude(c => c.TuyenDuong)
                .Where(t => t.Ve != null && t.Ve.NguoiDungId == id)
                .OrderByDescending(t => t.NgayThanhToan)
                .ToListAsync();

            ViewBag.User = user;
            ViewBag.Transactions = transactions;

            return View();
        }

        // GET: Admin/UserManagement/ExportUsers
        public async Task<IActionResult> ExportUsers()
        {
            var users = await _context.NguoiDungs.ToListAsync();
            
            var csv = new StringBuilder();
            csv.AppendLine("ID,Họ tên,Email,Số điện thoại,Ngày đăng ký,Là Admin,Trạng thái,Tài khoản bị khoá");
            
            foreach (var user in users)
            {
                csv.AppendLine($"{user.NguoiDungId}," +
                              $"\"{user.HoTen}\"," +
                              $"\"{user.Email}\"," +
                              $"\"{user.SoDienThoai ?? ""}\"," +
                              $"{user.NgayDangKy:dd/MM/yyyy}," +
                              $"{(user.LaAdmin ? "Có" : "Không")}," +
                              $"{(user.TrangThaiHoatDong ? "Hoạt động" : "Không hoạt động")}," +
                              $"{(user.TaiKhoanBiKhoa ? "Có" : "Không")}");
            }

            var bytes = Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", $"DanhSachNguoiDung_{DateTime.Now:yyyyMMdd}.csv");
        }

        private bool UserExists(int id)
        {
            return _context.NguoiDungs.Any(e => e.NguoiDungId == id);
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256Hash = SHA256.Create())
            {
                byte[] bytes = sha256Hash.ComputeHash(Encoding.UTF8.GetBytes(password));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
