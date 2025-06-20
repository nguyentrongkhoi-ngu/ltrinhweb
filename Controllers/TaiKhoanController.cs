using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using System.Security.Claims;

namespace DatVeXe.Controllers
{
    public class TaiKhoanController : Controller
    {
        private readonly DatVeXeContext _context;
        public TaiKhoanController(DatVeXeContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult DangNhap()
        {
            // Nếu người dùng đã đăng nhập, chuyển hướng về trang chủ
            if (HttpContext.Session.GetInt32("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }

            // Kiểm tra thông báo từ middleware
            var tempMessage = HttpContext.Session.GetString("TempMessage");
            if (!string.IsNullOrEmpty(tempMessage))
            {
                TempData["ThongBao"] = tempMessage;
                HttpContext.Session.Remove("TempMessage");
            }

            return View();
        }

        [HttpPost]
        public IActionResult DangNhap(string email, string matKhau)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(matKhau))
            {
                TempData["ThongBao"] = "Vui lòng nhập đầy đủ thông tin!";
                return View();
            }

            // Mã hóa mật khẩu để so sánh
            string hashedPassword = HashPassword(matKhau);

            // Tìm người dùng theo email và mật khẩu
            var user = _context.NguoiDungs.FirstOrDefault(u => u.Email == email && u.MatKhau == hashedPassword);

            // Nếu không tìm thấy, thử tìm với mật khẩu chưa mã hóa (cho dữ liệu cũ)
            if (user == null)
            {
                user = _context.NguoiDungs.FirstOrDefault(u => u.Email == email && u.MatKhau == matKhau);

                // Nếu tìm thấy với mật khẩu chưa mã hóa, cập nhật mật khẩu thành mã hóa
                if (user != null)
                {
                    user.MatKhau = hashedPassword;
                    _context.SaveChanges();
                }
            }

            if (user != null)
            {
                // Kiểm tra trạng thái hoạt động của tài khoản
                if (!user.TrangThaiHoatDong)
                {
                    TempData["ThongBao"] = "Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên để được hỗ trợ.";
                    return View();
                }

                // Kiểm tra tài khoản có bị khóa không
                if (user.TaiKhoanBiKhoa)
                {
                    var lockMessage = "Tài khoản của bạn đã bị khóa.";
                    if (!string.IsNullOrEmpty(user.LyDoKhoa))
                    {
                        lockMessage += $" Lý do: {user.LyDoKhoa}";
                    }
                    lockMessage += " Vui lòng liên hệ quản trị viên để được hỗ trợ.";
                    TempData["ThongBao"] = lockMessage;
                    return View();
                }

                // Cập nhật thời gian đăng nhập cuối
                user.LanDangNhapCuoi = DateTime.Now;
                _context.SaveChanges();

                // Lưu thông tin người dùng vào session
                HttpContext.Session.SetInt32("UserId", user.NguoiDungId);
                HttpContext.Session.SetString("UserName", user.HoTen);
                HttpContext.Session.SetString("UserEmail", user.Email);
                HttpContext.Session.SetInt32("IsAdmin", user.LaAdmin ? 1 : 0);

                TempData["ThongBao"] = "Đăng nhập thành công!";
                return RedirectToAction("Index", "Home");
            }

            TempData["ThongBao"] = "Sai email hoặc mật khẩu!";
            return View();
        }

        [HttpGet]
        public IActionResult DangKy()
        {
            // Nếu người dùng đã đăng nhập, chuyển hướng về trang chủ
            if (HttpContext.Session.GetInt32("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpPost]
        public IActionResult DangKy(string email, string matKhau, string xacNhanMatKhau, string hoTen, string soDienThoai)
        {
            // Kiểm tra dữ liệu đầu vào
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(matKhau) ||
                string.IsNullOrEmpty(xacNhanMatKhau) || string.IsNullOrEmpty(hoTen) || string.IsNullOrEmpty(soDienThoai))
            {
                TempData["ThongBao"] = "Vui lòng nhập đầy đủ thông tin!";
                return View();
            }

            // Kiểm tra định dạng số điện thoại
            if (!System.Text.RegularExpressions.Regex.IsMatch(soDienThoai, @"^0[0-9]{9}$"))
            {
                TempData["ThongBao"] = "Số điện thoại không hợp lệ! Phải bắt đầu bằng số 0 và có 10 chữ số.";
                return View();
            }

            // Kiểm tra mật khẩu xác nhận
            if (matKhau != xacNhanMatKhau)
            {
                TempData["ThongBao"] = "Mật khẩu xác nhận không khớp!";
                return View();
            }

            // Kiểm tra email đã tồn tại
            if (_context.NguoiDungs.Any(u => u.Email == email))
            {
                TempData["ThongBao"] = "Email đã tồn tại!";
                return View();
            }

            // Mã hóa mật khẩu
            string hashedPassword = HashPassword(matKhau);

            // Tạo người dùng mới
            var user = new NguoiDung {
                Email = email,
                MatKhau = hashedPassword,
                HoTen = hoTen,
                SoDienThoai = soDienThoai,
                LaAdmin = false
            };

            _context.NguoiDungs.Add(user);
            _context.SaveChanges();

            // Kiểm tra trạng thái tài khoản trước khi đăng nhập tự động
            if (!user.TrangThaiHoatDong)
            {
                TempData["ThongBao"] = "Đăng ký thành công nhưng tài khoản đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên.";
                return RedirectToAction("DangNhap");
            }

            if (user.TaiKhoanBiKhoa)
            {
                TempData["ThongBao"] = "Đăng ký thành công nhưng tài khoản đã bị khóa. Vui lòng liên hệ quản trị viên.";
                return RedirectToAction("DangNhap");
            }

            // Đăng nhập tự động sau khi đăng ký
            HttpContext.Session.SetInt32("UserId", user.NguoiDungId);
            HttpContext.Session.SetString("UserName", user.HoTen);
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetInt32("IsAdmin", 0); // Người dùng mới không phải admin

            TempData["ThongBao"] = "Đăng ký thành công!";
            return RedirectToAction("Index", "Home");
        }

        [HttpGet]
        public IActionResult Auth()
        {
            // Nếu người dùng đã đăng nhập, chuyển hướng về trang chủ
            if (HttpContext.Session.GetInt32("UserId") != null)
            {
                return RedirectToAction("Index", "Home");
            }
            return View();
        }

        [HttpGet]
        public IActionResult DangXuat()
        {
            // Xóa tất cả session
            HttpContext.Session.Clear();
            return RedirectToAction("Index", "Home");
        }

        // Google Login
        [HttpGet]
        public IActionResult GoogleLogin()
        {
            var redirectUrl = Url.Action("GoogleCallback", "TaiKhoan");
            var properties = new AuthenticationProperties
            {
                RedirectUri = redirectUrl
            };

            // Thêm tham số prompt để luôn hiển thị màn hình chọn tài khoản
            properties.SetParameter("prompt", "select_account");

            return Challenge(properties, GoogleDefaults.AuthenticationScheme);
        }

        [HttpGet]
        public async Task<IActionResult> GoogleCallback()
        {
            try
            {
                var result = await HttpContext.AuthenticateAsync("Cookies");
                if (!result.Succeeded)
                {
                    TempData["ThongBao"] = "Đăng nhập Google thất bại!";
                    return RedirectToAction("DangNhap");
                }

                var claims = result.Principal.Claims;
                var googleId = claims.FirstOrDefault(c => c.Type == ClaimTypes.NameIdentifier)?.Value;
                var email = claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)?.Value;
                var name = claims.FirstOrDefault(c => c.Type == ClaimTypes.Name)?.Value;
                var picture = claims.FirstOrDefault(c => c.Type == "picture")?.Value;

                if (string.IsNullOrEmpty(googleId) || string.IsNullOrEmpty(email))
                {
                    TempData["ThongBao"] = "Không thể lấy thông tin từ Google!";
                    return RedirectToAction("DangNhap");
                }

                // Tìm user theo GoogleId hoặc Email
                var user = await _context.NguoiDungs
                    .FirstOrDefaultAsync(u => u.GoogleId == googleId || u.Email == email);

                if (user == null)
                {
                    // Tạo user mới
                    user = new NguoiDung
                    {
                        Email = email,
                        HoTen = name ?? "Google User",
                        GoogleId = googleId,
                        LoginProvider = "Google",
                        Avatar = picture,
                        MatKhau = HashPassword(Guid.NewGuid().ToString()), // Random password
                        LaAdmin = false,
                        TrangThaiHoatDong = true,
                        NgayDangKy = DateTime.Now
                    };

                    _context.NguoiDungs.Add(user);
                    await _context.SaveChangesAsync();
                }
                else if (string.IsNullOrEmpty(user.GoogleId))
                {
                    // Liên kết tài khoản hiện tại với Google
                    user.GoogleId = googleId;
                    user.LoginProvider = "Google";
                    user.Avatar = picture;
                    await _context.SaveChangesAsync();
                }

                // Kiểm tra trạng thái hoạt động của tài khoản
                if (!user.TrangThaiHoatDong)
                {
                    TempData["ThongBao"] = "Tài khoản của bạn đã bị vô hiệu hóa. Vui lòng liên hệ quản trị viên để được hỗ trợ.";
                    return RedirectToAction("DangNhap");
                }

                // Kiểm tra tài khoản có bị khóa không
                if (user.TaiKhoanBiKhoa)
                {
                    var lockMessage = "Tài khoản của bạn đã bị khóa.";
                    if (!string.IsNullOrEmpty(user.LyDoKhoa))
                    {
                        lockMessage += $" Lý do: {user.LyDoKhoa}";
                    }
                    lockMessage += " Vui lòng liên hệ quản trị viên để được hỗ trợ.";
                    TempData["ThongBao"] = lockMessage;
                    return RedirectToAction("DangNhap");
                }

                // Cập nhật thông tin đăng nhập
                user.LanDangNhapCuoi = DateTime.Now;
                await _context.SaveChangesAsync();

                // Lưu thông tin vào session
                HttpContext.Session.SetInt32("UserId", user.NguoiDungId);
                HttpContext.Session.SetString("UserName", user.HoTen);
                HttpContext.Session.SetString("UserEmail", user.Email);
                HttpContext.Session.SetInt32("IsAdmin", user.LaAdmin ? 1 : 0);

                TempData["ThongBao"] = "Đăng nhập Google thành công!";
                return RedirectToAction("Index", "Home");
            }
            catch (Exception ex)
            {
                TempData["ThongBao"] = "Có lỗi xảy ra khi đăng nhập Google: " + ex.Message;
                return RedirectToAction("DangNhap");
            }
        }

        [HttpGet]
        public async Task<IActionResult> Dashboard()
        {
            // Check if user is logged in
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("DangNhap");
            }

            var user = await _context.NguoiDungs.FirstOrDefaultAsync(u => u.NguoiDungId == userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("DangNhap");
            }

            // Thống kê vé của người dùng
            var userTickets = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.Xe)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .Where(v => v.NguoiDungId == userId.Value)
                .OrderByDescending(v => v.NgayDat)
                .ToListAsync();

            // Tính toán thống kê
            var totalTickets = userTickets.Count;
            var totalSpent = userTickets
                .Where(v => v.ThanhToans != null && v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong))
                .Sum(v => v.GiaVe);

            var upcomingTrips = userTickets
                .Count(v => v.VeTrangThai == TrangThaiVe.DaDat && v.ChuyenXe.NgayKhoiHanh > DateTime.Now);

            var completedTrips = userTickets
                .Count(v => v.VeTrangThai == TrangThaiVe.DaDat && v.ChuyenXe.NgayKhoiHanh <= DateTime.Now);

            var cancelledTickets = userTickets
                .Count(v => v.VeTrangThai == TrangThaiVe.DaHuy);

            // Lấy 5 vé gần nhất
            var recentTickets = userTickets.Take(5).ToList();

            ViewBag.User = user;
            ViewBag.TotalTickets = totalTickets;
            ViewBag.TotalSpent = totalSpent;
            ViewBag.UpcomingTrips = upcomingTrips;
            ViewBag.CompletedTrips = completedTrips;
            ViewBag.CancelledTickets = cancelledTickets;
            ViewBag.RecentTickets = recentTickets;

            return View();
        }



        [HttpGet]
        public IActionResult Profile()
        {
            // Check if user is logged in
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("DangNhap");
            }

            var user = _context.NguoiDungs.FirstOrDefault(u => u.NguoiDungId == userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("DangNhap");
            }

            // Don't show password
            user.MatKhau = "";
            return View(user);
        }

        [HttpPost]
        public IActionResult Profile(string hoTen, string email, string matKhauCu, string matKhauMoi, string xacNhanMatKhau)
        {
            // Check if user is logged in
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId == null)
            {
                return RedirectToAction("DangNhap");
            }

            var user = _context.NguoiDungs.FirstOrDefault(u => u.NguoiDungId == userId.Value);
            if (user == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("DangNhap");
            }

            // Validate input
            if (string.IsNullOrEmpty(hoTen) || string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "Vui lòng nhập đầy đủ thông tin!";
                user.MatKhau = "";
                return View(user);
            }

            // Check if email already exists (excluding current user)
            if (_context.NguoiDungs.Any(u => u.Email == email && u.NguoiDungId != userId.Value))
            {
                TempData["Error"] = "Email đã tồn tại!";
                user.MatKhau = "";
                return View(user);
            }

            // If changing password, validate old password and new password
            if (!string.IsNullOrEmpty(matKhauMoi))
            {
                if (string.IsNullOrEmpty(matKhauCu))
                {
                    TempData["Error"] = "Vui lòng nhập mật khẩu cũ!";
                    user.MatKhau = "";
                    return View(user);
                }

                // Verify old password
                string hashedOldPassword = HashPassword(matKhauCu);
                if (user.MatKhau != hashedOldPassword)
                {
                    TempData["Error"] = "Mật khẩu cũ không đúng!";
                    user.MatKhau = "";
                    return View(user);
                }

                if (matKhauMoi != xacNhanMatKhau)
                {
                    TempData["Error"] = "Mật khẩu mới và xác nhận không khớp!";
                    user.MatKhau = "";
                    return View(user);
                }

                if (matKhauMoi.Length < 6)
                {
                    TempData["Error"] = "Mật khẩu mới phải có ít nhất 6 ký tự!";
                    user.MatKhau = "";
                    return View(user);
                }

                // Update password
                user.MatKhau = HashPassword(matKhauMoi);
            }

            // Update user info
            user.HoTen = hoTen;
            user.Email = email;

            try
            {
                _context.SaveChanges();

                // Update session
                HttpContext.Session.SetString("UserName", user.HoTen);
                HttpContext.Session.SetString("UserEmail", user.Email);

                TempData["Success"] = "Cập nhật thông tin thành công!";
            }
            catch (Exception)
            {
                TempData["Error"] = "Có lỗi xảy ra khi cập nhật thông tin!";
            }

            user.MatKhau = "";
            return View(user);
        }

        // Hàm mã hóa mật khẩu sử dụng SHA256
        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                // Chuyển đổi chuỗi thành mảng byte và tính toán hash
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));

                // Chuyển đổi mảng byte thành chuỗi hex
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
