using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace DatVeXe.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;
    private readonly DatVeXeContext _context;

    public HomeController(ILogger<HomeController> logger, DatVeXeContext context)
    {
        _logger = logger;
        _context = context;
    }

    public IActionResult Index()
    {
        // Kiểm tra người dùng đã đăng nhập chưa
        int? userId = HttpContext.Session.GetInt32("UserId");

        // Nếu đã đăng nhập, lấy thông tin người dùng và chuyến xe đã đặt
        if (userId.HasValue)
        {
            var user = _context.NguoiDungs.FirstOrDefault(u => u.NguoiDungId == userId.Value);
            if (user != null)
            {
                // Lấy danh sách vé đã đặt của người dùng
                var userTickets = _context.Ves
                    .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.Xe)
                    .Where(v => v.NguoiDungId == userId.Value)
                    .OrderByDescending(v => v.NgayDat)
                    .Take(3)
                    .ToList();                // Thống kê nhanh cho người dùng
                var totalUserTickets = _context.Ves.Count(v => v.NguoiDungId == userId.Value);
                var upcomingTrips = _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Count(v => v.NguoiDungId == userId.Value &&
                               v.VeTrangThai == TrangThaiVe.DaDat &&
                               v.ChuyenXe.NgayKhoiHanh > DateTime.Now);

                ViewBag.UserTickets = userTickets;
                ViewBag.TotalUserTickets = totalUserTickets;
                ViewBag.UpcomingTrips = upcomingTrips;
                ViewBag.UserName = user?.HoTen;
            }
        }

        // Lấy danh sách chuyến xe phổ biến
        var popularRoutes = _context.ChuyenXes
            .Include(c => c.Xe)
            .Include(c => c.TuyenDuong)
            .Include(c => c.Ves)
            .ToList()
            .OrderByDescending(c => c.Ves != null ? c.Ves.Count : 0)
            .Take(4)
            .ToList();

        ViewBag.PopularRoutes = popularRoutes;

        // Lấy danh sách điểm đi và điểm đến cho dropdown
        var diemDiList = _context.TuyenDuongs
            .Where(t => t.TrangThaiHoatDong)
            .Select(t => t.DiemDi)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        var diemDenList = _context.TuyenDuongs
            .Where(t => t.TrangThaiHoatDong)
            .Select(t => t.DiemDen)
            .Distinct()
            .OrderBy(d => d)
            .ToList();

        ViewBag.DiemDiList = diemDiList;
        ViewBag.DiemDenList = diemDenList;

        // Lấy thống kê cho trang chủ
        ViewBag.TotalUsers = _context.NguoiDungs.Count();
        ViewBag.TotalBuses = _context.Xes.Count();
        ViewBag.TotalRoutes = _context.TuyenDuongs.Count(t => t.TrangThaiHoatDong);
        ViewBag.TotalTickets = _context.Ves.Count();

        return View();
    }

    public IActionResult Privacy()
    {
        return View();
    }



    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
