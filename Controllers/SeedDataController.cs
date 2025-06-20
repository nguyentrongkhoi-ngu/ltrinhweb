using DatVeXe.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DatVeXe.Controllers
{
    public class SeedDataController : Controller
    {
        private readonly DatVeXeContext _context;

        public SeedDataController(DatVeXeContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            try
            {
                // Kiểm tra xem đã có dữ liệu chưa
                var hasData = await _context.Xes.AnyAsync() || 
                             await _context.TuyenDuongs.AnyAsync() || 
                             await _context.NguoiDungs.AnyAsync();

                if (hasData)
                {
                    ViewBag.Message = "Dữ liệu mẫu đã tồn tại trong hệ thống.";
                    ViewBag.MessageType = "warning";
                    return View();
                }

                // Thêm dữ liệu xe
                var xes = new List<Xe>
                {
                    new Xe { BienSo = "51A-12345", LoaiXe = "Giường nằm", SoGhe = 40 },
                    new Xe { BienSo = "51B-67890", LoaiXe = "Limousine", SoGhe = 20 },
                    new Xe { BienSo = "51C-11111", LoaiXe = "Ghế ngồi", SoGhe = 45 },
                    new Xe { BienSo = "51D-22222", LoaiXe = "Giường nằm VIP", SoGhe = 28 }
                };
                _context.Xes.AddRange(xes);
                await _context.SaveChangesAsync();

                // Thêm dữ liệu tuyến đường
                var tuyenDuongs = new List<TuyenDuong>
                {
                    new TuyenDuong 
                    { 
                        TenTuyen = "TP.HCM - Nha Trang", 
                        DiemDi = "TP.HCM", 
                        DiemDen = "Nha Trang", 
                        KhoangCach = 450, 
                        ThoiGianDuKien = new TimeSpan(8, 0, 0),
                        MoTa = "Tuyến đường từ TP.HCM đến Nha Trang",
                        NgayTao = new DateTime(2024, 1, 1)
                    },
                    new TuyenDuong 
                    { 
                        TenTuyen = "TP.HCM - Đà Lạt", 
                        DiemDi = "TP.HCM", 
                        DiemDen = "Đà Lạt", 
                        KhoangCach = 300, 
                        ThoiGianDuKien = new TimeSpan(6, 0, 0),
                        MoTa = "Tuyến đường từ TP.HCM đến Đà Lạt",
                        NgayTao = new DateTime(2024, 1, 1)
                    },
                    new TuyenDuong 
                    { 
                        TenTuyen = "TP.HCM - Vũng Tàu", 
                        DiemDi = "TP.HCM", 
                        DiemDen = "Vũng Tàu", 
                        KhoangCach = 125, 
                        ThoiGianDuKien = new TimeSpan(2, 30, 0),
                        MoTa = "Tuyến đường từ TP.HCM đến Vũng Tàu",
                        NgayTao = new DateTime(2024, 1, 1)
                    }
                };
                _context.TuyenDuongs.AddRange(tuyenDuongs);
                await _context.SaveChangesAsync();

                // Thêm dữ liệu người dùng
                var nguoiDungs = new List<NguoiDung>
                {
                    new NguoiDung 
                    { 
                        HoTen = "Admin", 
                        Email = "admin@datvexe.com", 
                        MatKhau = "admin123", 
                        LaAdmin = true,
                        NgayDangKy = new DateTime(2024, 1, 1)
                    },
                    new NguoiDung 
                    { 
                        HoTen = "Nguyễn Văn A", 
                        Email = "a@gmail.com", 
                        MatKhau = "123456", 
                        LaAdmin = false,
                        NgayDangKy = new DateTime(2024, 1, 1)
                    },
                    new NguoiDung 
                    { 
                        HoTen = "Trần Thị B", 
                        Email = "b@gmail.com", 
                        MatKhau = "123456", 
                        LaAdmin = false,
                        NgayDangKy = new DateTime(2024, 1, 1)
                    }
                };
                _context.NguoiDungs.AddRange(nguoiDungs);
                await _context.SaveChangesAsync();

                // Lấy ID của các entity vừa tạo
                var xe1 = await _context.Xes.FirstAsync(x => x.BienSo == "51A-12345");
                var xe2 = await _context.Xes.FirstAsync(x => x.BienSo == "51B-67890");
                var xe3 = await _context.Xes.FirstAsync(x => x.BienSo == "51C-11111");
                
                var tuyen1 = await _context.TuyenDuongs.FirstAsync(t => t.TenTuyen == "TP.HCM - Nha Trang");
                var tuyen2 = await _context.TuyenDuongs.FirstAsync(t => t.TenTuyen == "TP.HCM - Đà Lạt");
                var tuyen3 = await _context.TuyenDuongs.FirstAsync(t => t.TenTuyen == "TP.HCM - Vũng Tàu");

                // Thêm dữ liệu chuyến xe
                var chuyenXes = new List<ChuyenXe>
                {
                    new ChuyenXe 
                    { 
                        TuyenDuongId = tuyen1.TuyenDuongId,
                        DiemDi = "TP.HCM", 
                        DiemDen = "Nha Trang", 
                        NgayKhoiHanh = new DateTime(2025, 6, 1, 8, 0, 0), 
                        XeId = xe1.XeId, 
                        Gia = 350000, 
                        ThoiGianDi = new TimeSpan(8, 0, 0),
                        NgayTao = new DateTime(2024, 1, 1)
                    },
                    new ChuyenXe 
                    { 
                        TuyenDuongId = tuyen2.TuyenDuongId,
                        DiemDi = "TP.HCM", 
                        DiemDen = "Đà Lạt", 
                        NgayKhoiHanh = new DateTime(2025, 6, 2, 7, 0, 0), 
                        XeId = xe2.XeId, 
                        Gia = 250000, 
                        ThoiGianDi = new TimeSpan(6, 0, 0),
                        NgayTao = new DateTime(2024, 1, 1)
                    },
                    new ChuyenXe 
                    { 
                        TuyenDuongId = tuyen3.TuyenDuongId,
                        DiemDi = "TP.HCM", 
                        DiemDen = "Vũng Tàu", 
                        NgayKhoiHanh = new DateTime(2025, 6, 3, 9, 0, 0), 
                        XeId = xe3.XeId, 
                        Gia = 120000, 
                        ThoiGianDi = new TimeSpan(2, 30, 0),
                        NgayTao = new DateTime(2024, 1, 1)
                    }
                };
                _context.ChuyenXes.AddRange(chuyenXes);
                await _context.SaveChangesAsync();

                // Thêm chỗ ngồi cho xe 1 (40 chỗ)
                var choNgois = new List<ChoNgoi>();
                for (int i = 1; i <= 40; i++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe1.XeId,
                        SoGhe = $"A{i:D2}",
                        Hang = (i - 1) / 4 + 1,
                        Cot = (i - 1) % 4 + 1,
                        LoaiGhe = "Giường nằm"
                    });
                }

                // Thêm chỗ ngồi cho xe 2 (20 chỗ)
                for (int i = 1; i <= 20; i++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe2.XeId,
                        SoGhe = $"L{i:D2}",
                        Hang = (i - 1) / 2 + 1,
                        Cot = (i - 1) % 2 + 1,
                        LoaiGhe = "Limousine"
                    });
                }

                // Thêm chỗ ngồi cho xe 3 (45 chỗ)
                for (int i = 1; i <= 45; i++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe3.XeId,
                        SoGhe = $"S{i:D2}",
                        Hang = (i - 1) / 5 + 1,
                        Cot = (i - 1) % 5 + 1,
                        LoaiGhe = "Ghế ngồi"
                    });
                }

                _context.ChoNgois.AddRange(choNgois);
                await _context.SaveChangesAsync();

                ViewBag.Message = "Dữ liệu mẫu đã được thêm thành công!";
                ViewBag.MessageType = "success";

                return View();
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Có lỗi xảy ra: {ex.Message}";
                ViewBag.MessageType = "danger";
                return View();
            }
        }

        [HttpPost]
        public async Task<IActionResult> AddMoreTrips()
        {
            try
            {
                // Thêm nhiều chuyến xe với ngày gần hơn
                var today = DateTime.Now;
                var chuyenXes = new List<ChuyenXe>();

                // Lấy xe và tuyến đường có sẵn
                var xes = await _context.Xes.ToListAsync();
                var tuyenDuongs = await _context.TuyenDuongs.ToListAsync();

                if (!xes.Any() || !tuyenDuongs.Any())
                {
                    ViewBag.Message = "Vui lòng chạy SeedData trước!";
                    ViewBag.MessageType = "warning";
                    return View("Index");
                }

                // Tạo chuyến xe cho 30 ngày tới
                for (int day = 0; day < 30; day++)
                {
                    var ngayKhoiHanh = today.AddDays(day);

                    // Tạo 3-5 chuyến xe mỗi ngày
                    for (int trip = 0; trip < 4; trip++)
                    {
                        var xe = xes[trip % xes.Count];
                        var tuyen = tuyenDuongs[trip % tuyenDuongs.Count];

                        var gioKhoiHanh = new int[] { 6, 8, 14, 20 }[trip];

                        // Tính giá dựa trên khoảng cách
                        decimal gia = tuyen.KhoangCach switch
                        {
                            <= 100 => 120000,  // Vũng Tàu
                            <= 300 => 250000,  // Đà Lạt
                            _ => 350000         // Nha Trang
                        };

                        chuyenXes.Add(new ChuyenXe
                        {
                            TuyenDuongId = tuyen.TuyenDuongId,
                            DiemDi = tuyen.DiemDi,
                            DiemDen = tuyen.DiemDen,
                            NgayKhoiHanh = new DateTime(ngayKhoiHanh.Year, ngayKhoiHanh.Month, ngayKhoiHanh.Day, gioKhoiHanh, 0, 0),
                            XeId = xe.XeId,
                            Gia = gia,
                            ThoiGianDi = tuyen.ThoiGianDuKien,
                            NgayTao = DateTime.Now
                        });
                    }
                }

                _context.ChuyenXes.AddRange(chuyenXes);
                await _context.SaveChangesAsync();

                ViewBag.Message = $"Đã thêm {chuyenXes.Count} chuyến xe mới!";
                ViewBag.MessageType = "success";

                return View("Index");
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Có lỗi xảy ra: {ex.Message}";
                ViewBag.MessageType = "danger";
                return View("Index");
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateSeatsForAllVehicles()
        {
            try
            {
                // Xóa tất cả chỗ ngồi cũ
                var oldSeats = await _context.ChoNgois.ToListAsync();
                _context.ChoNgois.RemoveRange(oldSeats);
                await _context.SaveChangesAsync();

                // Lấy tất cả xe
                var xes = await _context.Xes.ToListAsync();
                var choNgois = new List<ChoNgoi>();
                var debugInfo = new List<string>();

                foreach (var xe in xes)
                {
                    var seats = CreateSeatsForVehicle(xe);
                    choNgois.AddRange(seats);
                    debugInfo.Add($"Xe {xe.BienSo} ({xe.LoaiXe}): {xe.SoGhe} chỗ -> Tạo được {seats.Count} ghế");
                }

                _context.ChoNgois.AddRange(choNgois);
                await _context.SaveChangesAsync();

                ViewBag.Message = $"Đã tạo {choNgois.Count} chỗ ngồi cho {xes.Count} xe!\n" + string.Join("\n", debugInfo);
                ViewBag.MessageType = "success";

                return View("Index");
            }
            catch (Exception ex)
            {
                ViewBag.Message = $"Có lỗi xảy ra: {ex.Message}";
                ViewBag.MessageType = "danger";
                return View("Index");
            }
        }

        [HttpGet]
        public async Task<IActionResult> DebugSeats()
        {
            var xes = await _context.Xes.ToListAsync();
            var choNgois = await _context.ChoNgois.ToListAsync();

            var debugInfo = new List<string>();

            foreach (var xe in xes)
            {
                var xeSeats = choNgois.Where(c => c.XeId == xe.XeId).ToList();
                debugInfo.Add($"Xe {xe.BienSo} ({xe.LoaiXe}): {xe.SoGhe} chỗ định nghĩa, {xeSeats.Count} chỗ trong DB");

                if (xeSeats.Any())
                {
                    var maxHang = xeSeats.Max(c => c.Hang);
                    var maxCot = xeSeats.Max(c => c.Cot);
                    debugInfo.Add($"  - Max hàng: {maxHang}, Max cột: {maxCot}");

                    for (int h = 1; h <= Math.Min(maxHang, 3); h++)
                    {
                        var hangSeats = xeSeats.Where(c => c.Hang == h).OrderBy(c => c.Cot).ToList();
                        debugInfo.Add($"  - Hàng {h}: {string.Join(", ", hangSeats.Select(s => $"{s.SoGhe}(C{s.Cot})"))}");
                    }
                }
            }

            ViewBag.DebugInfo = debugInfo;
            return View("Index");
        }

        [HttpGet]
        public async Task<IActionResult> CheckVehicleData()
        {
            var xes = await _context.Xes.ToListAsync();
            var debugInfo = new List<string>();

            debugInfo.Add("=== THÔNG TIN XE ===");
            foreach (var xe in xes)
            {
                debugInfo.Add($"ID: {xe.XeId}, Biển số: {xe.BienSo}, Loại: {xe.LoaiXe}, Số ghế: {xe.SoGhe}");
            }

            debugInfo.Add("\n=== THỐNG KÊ CHỖ NGỒI ===");
            var choNgois = await _context.ChoNgois.ToListAsync();
            var groupedSeats = choNgois.GroupBy(c => c.XeId).ToList();

            foreach (var group in groupedSeats)
            {
                var xe = xes.FirstOrDefault(x => x.XeId == group.Key);
                if (xe != null)
                {
                    var seats = group.ToList();
                    debugInfo.Add($"Xe {xe.BienSo}: {seats.Count}/{xe.SoGhe} ghế");

                    if (seats.Count != xe.SoGhe)
                    {
                        debugInfo.Add($"  ⚠️ THIẾU {xe.SoGhe - seats.Count} ghế!");
                    }

                    var maxHang = seats.Any() ? seats.Max(s => s.Hang) : 0;
                    var maxCot = seats.Any() ? seats.Max(s => s.Cot) : 0;
                    debugInfo.Add($"  Layout: {maxHang} hàng x {maxCot} cột");
                }
            }

            // Kiểm tra xe không có ghế
            var xeKhongCoGhe = xes.Where(x => !groupedSeats.Any(g => g.Key == x.XeId)).ToList();
            if (xeKhongCoGhe.Any())
            {
                debugInfo.Add("\n=== XE CHƯA CÓ GHẾ ===");
                foreach (var xe in xeKhongCoGhe)
                {
                    debugInfo.Add($"⚠️ Xe {xe.BienSo} ({xe.LoaiXe}) chưa có ghế nào!");
                }
            }

            ViewBag.DebugInfo = debugInfo;
            return View("Index");
        }

        private List<ChoNgoi> CreateSeatsForVehicle(Xe xe)
        {
            var choNgois = new List<ChoNgoi>();

            switch (xe.LoaiXe.ToLower())
            {
                case "limousine":
                    // Limousine: 2 cột, ghế đôi, tối đa 20-24 chỗ
                    CreateLimousineSeats(choNgois, xe);
                    break;

                case "giường nằm":
                    // Giường nằm: 3 tầng, 2 bên, tối đa 40-46 chỗ
                    CreateSleeperSeats(choNgois, xe);
                    break;

                case "ghế ngồi":
                    // Ghế ngồi: 4-5 ghế/hàng, tối đa 45-50 chỗ
                    CreateRegularSeats(choNgois, xe);
                    break;

                case "vip":
                    // VIP: 3 ghế/hàng, rộng rãi, tối đa 30 chỗ
                    CreateVipSeats(choNgois, xe);
                    break;

                default:
                    // Mặc định: ghế ngồi thường
                    CreateRegularSeats(choNgois, xe);
                    break;
            }

            return choNgois;
        }

        private void CreateLimousineSeats(List<ChoNgoi> choNgois, Xe xe)
        {
            int seatCount = 0;
            int hang = 1;

            while (seatCount < xe.SoGhe)
            {
                // Cột 1 (bên trái)
                if (seatCount < xe.SoGhe)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"L{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = 1,
                        LoaiGhe = "Limousine",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                // Cột 3 (bên phải, để lại cột 2 làm lối đi)
                if (seatCount < xe.SoGhe)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"L{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = 3,
                        LoaiGhe = "Limousine",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                hang++;
            }
        }

        private void CreateSleeperSeats(List<ChoNgoi> choNgois, Xe xe)
        {
            int seatCount = 0;
            int hang = 1;

            // Giường nằm: 6 giường mỗi hàng (3 tầng x 2 bên)
            // Tính số hàng cần thiết
            int soHangCanThiet = (int)Math.Ceiling((double)xe.SoGhe / 6);

            for (int h = 1; h <= soHangCanThiet && seatCount < xe.SoGhe; h++)
            {
                // Bên trái: cột 1,2,3 (tầng dưới, giữa, trên)
                for (int tang = 1; tang <= 3 && seatCount < xe.SoGhe; tang++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"A{seatCount + 1:D2}",
                        Hang = h,
                        Cot = tang,
                        LoaiGhe = "Giường nằm",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                // Bên phải: cột 5,6,7 (tầng dưới, giữa, trên) - cột 4 là lối đi
                for (int tang = 5; tang <= 7 && seatCount < xe.SoGhe; tang++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"A{seatCount + 1:D2}",
                        Hang = h,
                        Cot = tang,
                        LoaiGhe = "Giường nằm",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }
            }
        }

        private void CreateRegularSeats(List<ChoNgoi> choNgois, Xe xe)
        {
            int seatCount = 0;
            int hang = 1;
            int seatsPerRow = 5; // 2 + 3 (2 bên trái, lối đi, 3 bên phải)

            while (seatCount < xe.SoGhe)
            {
                // Bên trái: 2 ghế (cột 1,2)
                for (int cot = 1; cot <= 2 && seatCount < xe.SoGhe; cot++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"S{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = cot,
                        LoaiGhe = "Ghế ngồi",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                // Bên phải: 3 ghế (cột 4,5,6 - cột 3 là lối đi)
                for (int cot = 4; cot <= 6 && seatCount < xe.SoGhe; cot++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"S{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = cot,
                        LoaiGhe = "Ghế ngồi",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                hang++;
            }
        }

        private void CreateVipSeats(List<ChoNgoi> choNgois, Xe xe)
        {
            int seatCount = 0;
            int hang = 1;

            while (seatCount < xe.SoGhe)
            {
                // VIP: 1 + 2 (1 bên trái, lối đi, 2 bên phải)
                // Bên trái: 1 ghế (cột 1)
                if (seatCount < xe.SoGhe)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"V{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = 1,
                        LoaiGhe = "VIP",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                // Bên phải: 2 ghế (cột 3,4 - cột 2 là lối đi)
                for (int cot = 3; cot <= 4 && seatCount < xe.SoGhe; cot++)
                {
                    choNgois.Add(new ChoNgoi
                    {
                        XeId = xe.XeId,
                        SoGhe = $"V{seatCount + 1:D2}",
                        Hang = hang,
                        Cot = cot,
                        LoaiGhe = "VIP",
                        TrangThaiHoatDong = true
                    });
                    seatCount++;
                }

                hang++;
            }
        }
    }
}
