using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using DatVeXe.Services;
using DatVeXe.Extensions;
using DatVeXe.Helpers;
using System.Text.Json;
using System.Text.Json.Serialization;
using QRCoder;

namespace DatVeXe.Controllers
{
    public class BookingController : Controller
    {
        private readonly DatVeXeContext _context;
        private readonly IEmailService _emailService;
        private readonly ISMSService _smsService;
        private readonly IPaymentService _paymentService;
        private readonly ITripSearchService _tripSearchService;
        private readonly ILogger<BookingController> _logger;
        private readonly IQRCodeService _qrCodeService;
        private readonly ITicketCancellationService _cancellationService;

        public BookingController(DatVeXeContext context, IEmailService emailService, ISMSService smsService,
            IPaymentService paymentService, ITripSearchService tripSearchService, ILogger<BookingController> logger,
            IQRCodeService qrCodeService, ITicketCancellationService cancellationService)
        {
            _context = context;
            _emailService = emailService;
            _smsService = smsService;
            _paymentService = paymentService;
            _tripSearchService = tripSearchService;
            _logger = logger;
            _qrCodeService = qrCodeService;
            _cancellationService = cancellationService;
        }

        private string GenerateTicketCode()
        {
            return $"VE{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(100, 999)}";
        }

        private string GenerateTransactionCode()
        {
            return $"TXN{DateTime.Now:yyyyMMddHHmmss}{new Random().Next(1000, 9999)}";
        }

        // GET: Booking/Search - Tìm kiếm chuyến xe
        public async Task<IActionResult> Search()
        {
            var viewModel = new BookingSearchViewModel();

            // Load danh sách điểm đi/đến từ service
            ViewBag.DiemDiList = await _tripSearchService.GetDepartureLocationsAsync();
            ViewBag.DiemDenList = await _tripSearchService.GetDestinationLocationsAsync();
            ViewBag.NhaXeList = await _tripSearchService.GetBusCompaniesAsync();
            ViewBag.LoaiXeList = await _tripSearchService.GetBusTypesAsync();

            return View(viewModel);
        }

        // POST: Booking/Search - Xử lý tìm kiếm
        [HttpPost]
        public async Task<IActionResult> Search(BookingSearchViewModel model)
        {
            if (!ModelState.IsValid)
            {
                // Reload dropdown data
                ViewBag.DiemDiList = await _tripSearchService.GetDepartureLocationsAsync();
                ViewBag.DiemDenList = await _tripSearchService.GetDestinationLocationsAsync();
                ViewBag.NhaXeList = await _tripSearchService.GetBusCompaniesAsync();
                ViewBag.LoaiXeList = await _tripSearchService.GetBusTypesAsync();
                return View(model);
            }

            // Sử dụng service để tìm kiếm
            model.KetQua = await _tripSearchService.SearchTripsAsync(model);
            model.TotalResults = model.KetQua.Count;
            model.HasSearched = true;

            // Reload dropdown data for view
            ViewBag.DiemDiList = await _tripSearchService.GetDepartureLocationsAsync();
            ViewBag.DiemDenList = await _tripSearchService.GetDestinationLocationsAsync();
            ViewBag.NhaXeList = await _tripSearchService.GetBusCompaniesAsync();
            ViewBag.LoaiXeList = await _tripSearchService.GetBusTypesAsync();

            return View(model);
        }

        // GET: Booking/SelectTrip/{id} - Chọn chuyến xe và bắt đầu quy trình đặt vé
        public async Task<IActionResult> SelectTrip(int id)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.Ves)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == id);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction("Search");
            }

            if (chuyenXe.NgayKhoiHanh <= DateTime.Now)
            {
                TempData["Error"] = "Chuyến xe đã khởi hành";
                return RedirectToAction("Search");
            }

            // Tạo session booking mới
            var sessionId = Guid.NewGuid().ToString();
            var bookingStep = new BookingStepViewModel
            {
                CurrentStep = 2, // Chuyển đến bước chọn ghế
                SessionId = sessionId,
                ChuyenXeId = id,
                ChuyenXe = null, // Không lưu entity vào session để tránh circular reference
                TongTien = chuyenXe.Gia
            };

            // Lưu vào session với JsonSerializerOptions để tránh circular reference
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = false
            };
            HttpContext.Session.SetString($"Booking_{sessionId}", JsonSerializer.Serialize(bookingStep, options));

            return RedirectToAction("SelectSeat", new { sessionId });
        }

        // GET: Booking/SelectSeat - Chọn ghế ngồi
        public async Task<IActionResult> SelectSeat(string sessionId)
        {
            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || !bookingStep.ChuyenXeId.HasValue)
            {
                TempData["Error"] = "Phiên đặt vé không hợp lệ";
                return RedirectToAction("Search");
            }

            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .Include(c => c.Ves)
                .ThenInclude(v => v.ChoNgoi)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy chuyến xe";
                return RedirectToAction("Search");
            }

            // Cập nhật ChuyenXe vào booking step
            bookingStep.ChuyenXe = chuyenXe;

            // Lấy danh sách ghế
            var choNgois = await _context.ChoNgois
                .Where(c => c.XeId == chuyenXe.XeId && c.TrangThaiHoatDong)
                .OrderBy(c => c.Hang)
                .ThenBy(c => c.Cot)
                .ToListAsync();

            // Lấy ghế đã đặt (bao gồm tất cả trạng thái đã sử dụng ghế)
            var veDaDat = chuyenXe.Ves?.Where(v => v.VeTrangThai == TrangThaiVe.DaDat ||
                                                  v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                                                  v.VeTrangThai == TrangThaiVe.DaSuDung).ToList() ?? new List<Ve>();

            // Lấy ghế đang được giữ
            var activeReservations = await _context.SeatReservations
                .Where(r => r.ChuyenXeId == chuyenXe.ChuyenXeId &&
                           r.IsActive &&
                           r.ExpiresAt > DateTime.Now)
                .ToListAsync();

            var viewModel = new SeatSelectionViewModel
            {
                ChuyenXeId = chuyenXe.ChuyenXeId,
                ChuyenXe = chuyenXe,
                Xe = chuyenXe.Xe!,
                SessionId = sessionId,
                SoHang = choNgois.Any() ? choNgois.Max(c => c.Hang) : 0,
                SoCot = choNgois.Any() ? choNgois.Max(c => c.Cot) : 0,
                LoaiXe = chuyenXe.Xe?.LoaiXe ?? "",
                DanhSachGhe = choNgois.Select(c => new ChoNgoiViewModel
                {
                    ChoNgoiId = c.ChoNgoiId,
                    SoGhe = c.SoGhe,
                    Hang = c.Hang,
                    Cot = c.Cot,
                    LoaiGhe = c.LoaiGhe,
                    TrangThaiHoatDong = c.TrangThaiHoatDong,
                    DaDat = veDaDat.Any(v => v.ChoNgoiId == c.ChoNgoiId),
                    DangGiu = activeReservations.Any(r => r.ChoNgoiId == c.ChoNgoiId && r.SessionId != sessionId),
                    TenKhachDat = veDaDat.FirstOrDefault(v => v.ChoNgoiId == c.ChoNgoiId)?.TenKhach
                }).ToList()
            };

            return View(viewModel);
        }

        // POST: Booking/SelectSeat - Xử lý chọn ghế
        [HttpPost]
        public async Task<IActionResult> SelectSeat(string sessionId, int seatId)
        {
            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || !bookingStep.IsStep1Valid())
            {
                return Json(new { success = false, message = "Phiên đặt vé không hợp lệ" });
            }

            // Kiểm tra ghế có tồn tại và khả dụng
            var choNgoi = await _context.ChoNgois
                .FirstOrDefaultAsync(c => c.ChoNgoiId == seatId && c.TrangThaiHoatDong);

            if (choNgoi == null)
            {
                return Json(new { success = false, message = "Ghế không tồn tại hoặc không khả dụng" });
            }

            // Kiểm tra ghế đã được đặt chưa (bao gồm tất cả trạng thái đã sử dụng ghế)
            var daDat = await _context.Ves
                .AnyAsync(v => v.ChuyenXeId == bookingStep.ChuyenXeId &&
                              v.ChoNgoiId == seatId &&
                              (v.VeTrangThai == TrangThaiVe.DaDat ||
                               v.VeTrangThai == TrangThaiVe.DaThanhToan ||
                               v.VeTrangThai == TrangThaiVe.DaSuDung));

            if (daDat)
            {
                return Json(new { success = false, message = "Ghế đã được đặt" });
            }

            // Kiểm tra ghế đang được giữ bởi session khác
            var dangGiu = await _context.SeatReservations
                .AnyAsync(r => r.ChuyenXeId == bookingStep.ChuyenXeId &&
                              r.ChoNgoiId == seatId &&
                              r.IsActive &&
                              r.ExpiresAt > DateTime.Now &&
                              r.SessionId != sessionId);

            if (dangGiu)
            {
                return Json(new { success = false, message = "Ghế đang được giữ bởi khách hàng khác" });
            }

            // Hủy reservation cũ của session này
            var oldReservations = await _context.SeatReservations
                .Where(r => r.ChuyenXeId == bookingStep.ChuyenXeId &&
                           r.SessionId == sessionId &&
                           r.IsActive)
                .ToListAsync();

            foreach (var old in oldReservations)
            {
                old.IsActive = false;
            }

            // Tạo reservation mới
            var reservation = new SeatReservation
            {
                ChuyenXeId = bookingStep.ChuyenXeId.Value,
                ChoNgoiId = seatId,
                SessionId = sessionId,
                UserEmail = HttpContext.Session.GetString("UserEmail"),
                ReservedAt = DateTime.Now,
                ExpiresAt = DateTime.Now.AddMinutes(5),
                IsActive = true
            };

            _context.SeatReservations.Add(reservation);
            await _context.SaveChangesAsync();

            // Cập nhật booking step
            bookingStep.CurrentStep = 3;
            bookingStep.ChoNgoiId = seatId;
            bookingStep.ChoNgoi = choNgoi;

            // Lưu lại vào session
            HttpContext.Session.SetString($"Booking_{sessionId}", JsonSerializer.Serialize(bookingStep));

            return Json(new { 
                success = true, 
                message = "Đã chọn ghế thành công",
                nextUrl = Url.Action("PassengerInfo", new { sessionId })
            });
        }

        // Helper method để lấy booking từ session
        private BookingStepViewModel? GetBookingFromSession(string sessionId)
        {
            var sessionData = HttpContext.Session.GetString($"Booking_{sessionId}");
            if (string.IsNullOrEmpty(sessionData))
                return null;

            try
            {
                var options = new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.IgnoreCycles,
                    WriteIndented = false
                };
                return JsonSerializer.Deserialize<BookingStepViewModel>(sessionData, options);
            }
            catch
            {
                return null;
            }
        }

        // Helper method để lưu booking vào session
        private void SaveBookingToSession(string sessionId, BookingStepViewModel booking)
        {
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.IgnoreCycles,
                WriteIndented = false
            };
            HttpContext.Session.SetString($"Booking_{sessionId}", JsonSerializer.Serialize(booking, options));
        }

        // GET: Booking/PassengerInfo - Nhập thông tin hành khách
        public async Task<IActionResult> PassengerInfo(string sessionId)
        {
            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || !bookingStep.IsStep2Valid())
            {
                TempData["Error"] = "Vui lòng chọn ghế trước";
                return RedirectToAction("SelectSeat", new { sessionId });
            }

            // Load fresh data from database
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

            var choNgoi = await _context.ChoNgois
                .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

            if (chuyenXe == null || choNgoi == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin chuyến xe hoặc ghế";
                return RedirectToAction("Search");
            }

            var viewModel = new PassengerInfoViewModel
            {
                ChuyenXeId = bookingStep.ChuyenXeId.Value,
                ChoNgoiId = bookingStep.ChoNgoiId.Value,
                ChuyenXe = chuyenXe,
                ChoNgoi = choNgoi
            };

            // Nếu user đã đăng nhập, tự động điền thông tin và load danh bạ
            int? userId = HttpContext.Session.GetInt32("UserId");
            if (userId.HasValue)
            {
                var user = await _context.NguoiDungs.FindAsync(userId.Value);
                if (user != null)
                {
                    viewModel.TenKhach = user.HoTen;
                    viewModel.SoDienThoai = user.SoDienThoai;
                    viewModel.Email = user.Email;
                }

                // Load danh bạ hành khách
                ViewBag.DanhBaHanhKhach = await _context.DanhBaHanhKhachs
                    .Where(d => d.NguoiDungId == userId.Value)
                    .OrderByDescending(d => d.SoLanSuDung)
                    .ThenBy(d => d.TenHanhKhach)
                    .Take(10)
                    .ToListAsync();
            }

            // Truyền sessionId vào ViewBag
            ViewBag.SessionId = sessionId;

            return View(viewModel);
        }

        // POST: Booking/PassengerInfo - Xử lý thông tin hành khách
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PassengerInfo(string sessionId,
            [Bind("ChuyenXeId,ChoNgoiId,TenKhach,SoDienThoai,Email,GhiChu,NhanSMS,NhanEmail")] PassengerInfoViewModel model)
        {
            _logger.LogInformation($"PassengerInfo POST called with sessionId: {sessionId}");
            _logger.LogInformation($"Model: TenKhach={model.TenKhach}, SoDienThoai={model.SoDienThoai}");

            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || !bookingStep.IsStep2Valid())
            {
                TempData["Error"] = "Phiên đặt vé không hợp lệ";
                return RedirectToAction("Search");
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState is invalid");
                foreach (var error in ModelState.Values.SelectMany(v => v.Errors))
                {
                    _logger.LogWarning($"Validation error: {error.ErrorMessage}");
                }

                // Reload data for view
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

                var choNgoi = await _context.ChoNgois
                    .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

                model.ChuyenXe = chuyenXe!;
                model.ChoNgoi = choNgoi!;
                return View(model);
            }

            // Cập nhật thông tin hành khách
            bookingStep.CurrentStep = 4;
            bookingStep.TenKhach = model.TenKhach;
            bookingStep.SoDienThoai = model.SoDienThoai;
            bookingStep.Email = model.Email;
            bookingStep.GhiChu = model.GhiChu;

            _logger.LogInformation($"Updated booking step 3 -> 4. TenKhach: {bookingStep.TenKhach}, SoDienThoai: {bookingStep.SoDienThoai}");

            SaveBookingToSession(sessionId, bookingStep);

            _logger.LogInformation($"Redirecting to Payment with sessionId: {sessionId}");
            return RedirectToAction("Payment", new { sessionId });
        }

        // GET: Booking/Payment - Chọn phương thức thanh toán
        public async Task<IActionResult> Payment(string sessionId)
        {
            _logger.LogInformation($"Payment GET called with sessionId: {sessionId}");

            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null)
            {
                _logger.LogWarning($"BookingStep is null for sessionId: {sessionId}");
                TempData["Error"] = "Phiên đặt vé không hợp lệ";
                return RedirectToAction("Search");
            }

            _logger.LogInformation($"BookingStep found. CurrentStep: {bookingStep.CurrentStep}, TenKhach: {bookingStep.TenKhach}, SoDienThoai: {bookingStep.SoDienThoai}");

            if (!bookingStep.IsStep3Valid())
            {
                _logger.LogWarning($"Step3 validation failed for sessionId: {sessionId}");
                TempData["Error"] = "Vui lòng nhập thông tin hành khách trước";
                return RedirectToAction("PassengerInfo", new { sessionId });
            }

            // Load fresh data from database
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

            var choNgoi = await _context.ChoNgois
                .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

            if (chuyenXe == null || choNgoi == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin chuyến xe hoặc ghế";
                return RedirectToAction("Search");
            }

            var viewModel = new PaymentSelectionViewModel
            {
                ChuyenXe = chuyenXe,
                ChoNgoi = choNgoi,
                TongTien = bookingStep.TongTien,
                PhiDichVu = 0, // Có thể tính phí dịch vụ tùy theo phương thức thanh toán
                ThanhTien = bookingStep.TongTien
            };

            // Set thông tin khách hàng
            viewModel.TenKhach = bookingStep.TenKhach;
            viewModel.SoDienThoai = bookingStep.SoDienThoai;
            viewModel.Email = bookingStep.Email;
            viewModel.GhiChu = bookingStep.GhiChu;
            viewModel.GiaVe = bookingStep.TongTien;

            // Truyền sessionId vào ViewBag
            ViewBag.SessionId = sessionId;

            return View(viewModel);
        }

        // POST: Booking/Payment - Xử lý thanh toán
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Payment(string sessionId, [Bind("PhuongThucThanhToan,DongYDieuKhoan,TongTien,PhiDichVu,ThanhTien,MaKhuyenMai,GiamGia,TenKhach,SoDienThoai,Email,GhiChu")] PaymentSelectionViewModel model)
        {
            _logger.LogInformation($"Payment POST called with sessionId: {sessionId}");
            _logger.LogInformation($"Model: PhuongThucThanhToan={model.PhuongThucThanhToan}, DongYDieuKhoan={model.DongYDieuKhoan}, ThanhTien={model.ThanhTien}");

            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || !bookingStep.IsStep3Valid())
            {
                _logger.LogWarning("BookingStep is null or invalid");
                TempData["Error"] = "Phiên đặt vé không hợp lệ";
                return RedirectToAction("Search");
            }

            // Clear navigation property validation errors
            var keysToRemove = ModelState.Keys.Where(k => k.StartsWith("ChoNgoi.") || k.StartsWith("ChuyenXe.")).ToList();
            foreach (var key in keysToRemove)
            {
                ModelState.Remove(key);
            }

            if (!ModelState.IsValid)
            {
                _logger.LogWarning("ModelState is invalid");
                foreach (var error in ModelState)
                {
                    foreach (var subError in error.Value.Errors)
                    {
                        _logger.LogWarning($"Validation error: {error.Key} - {subError.ErrorMessage}");
                    }
                }
                // Reload data for view
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

                var choNgoi = await _context.ChoNgois
                    .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

                model.ChuyenXe = chuyenXe!;
                model.ChoNgoi = choNgoi!;
                model.TongTien = bookingStep.TongTien;
                model.ThanhTien = bookingStep.TongTien;
                return View(model);
            }

            try
            {
                _logger.LogInformation($"Bắt đầu tạo booking cho session {sessionId}");

                // Tạo mã booking
                var maBooking = GenerateTicketCode();

                // Lưu thông tin booking vào session để sử dụng sau
                bookingStep.MaVe = maBooking;

                // Cập nhật session
                HttpContext.Session.SetString($"Booking_{sessionId}", System.Text.Json.JsonSerializer.Serialize(bookingStep));

                _logger.LogInformation($"Đã tạo booking thành công với mã {maBooking}");

                // Lấy giá vé từ chuyến xe
                var chuyenXeDb = await _context.ChuyenXes.FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);
                var giaVeGoc = chuyenXeDb != null ? chuyenXeDb.Gia : 0;
                // Tạo vé trước - Lưu giá sau giảm (ThanhTien) thay vì giá gốc
                var userId = HttpContext.Session.GetInt32("UserId");
                var ve = new Ve
                {
                    MaVe = maBooking,
                    ChuyenXeId = bookingStep.ChuyenXeId.Value,
                    ChoNgoiId = bookingStep.ChoNgoiId,
                    TenKhach = bookingStep.TenKhach,
                    SoDienThoai = bookingStep.SoDienThoai,
                    Email = bookingStep.Email,
                    GiaVe = model.ThanhTien, // Lưu giá sau giảm thay vì giá gốc
                    NgayDat = DateTime.Now,
                    TrangThai = TrangThaiVe.DaDat, // Sử dụng DaDat thay vì DaXacNhan
                    GhiChu = !string.IsNullOrEmpty(model.MaKhuyenMai) ?
                        $"Booking {maBooking} - Áp dụng mã {model.MaKhuyenMai} giảm {model.GiamGia:N0} VNĐ" :
                        $"Booking {maBooking}",
                    NguoiDungId = userId.HasValue ? userId.Value : (int?)null
                };

                _context.Ves.Add(ve);
                await _context.SaveChangesAsync(); // Save để có VeId

                // Tạo giao dịch thanh toán
                var thanhToan = new ThanhToan
                {
                    VeId = ve.VeId, // Liên kết với Ve vừa tạo
                    MaGiaoDich = GenerateTransactionCode(),
                    PhuongThucThanhToan = model.PhuongThucThanhToan,
                    SoTien = model.ThanhTien,
                    TrangThai = model.PhuongThucThanhToan == PhuongThucThanhToan.TaiQuay ?
                               TrangThaiThanhToan.ThanhCong : TrangThaiThanhToan.DangXuLy,
                    ThoiGianTao = DateTime.Now,
                    NgayThanhToan = DateTime.Now,
                    GhiChu = $"Thanh toán booking {maBooking}"
                };

                _context.ThanhToans.Add(thanhToan);
                await _context.SaveChangesAsync();

                // Cập nhật booking step với thông tin thanh toán
                bookingStep.MaVe = ve.MaVe;
                bookingStep.MaGiaoDich = thanhToan.MaGiaoDich;
                SaveBookingToSession(sessionId, bookingStep);

                // Lưu lịch sử sử dụng mã khuyến mãi nếu có (chỉ thực hiện tại đây, không lặp lại ở cuối hàm)
                if (!string.IsNullOrEmpty(model.MaKhuyenMai))
                {
                    var promo = await _context.KhuyenMais.FirstOrDefaultAsync(k => k.MaKhuyenMai == model.MaKhuyenMai);
                    if (promo != null)
                    {
                        promo.SoLuongDaSuDung++;
                        var lichSu = new LichSuKhuyenMai
                        {
                            NguoiDungId = userId,
                            VeId = ve.VeId,
                            KhuyenMaiId = promo.KhuyenMaiId,
                            MaKhuyenMai = promo.MaKhuyenMai,
                            ThoiGianSuDung = DateTime.Now,
                            GiaTriGiam = model.GiamGia
                        };
                        _context.LichSuKhuyenMais.Add(lichSu);
                        await _context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi tạo vé và thanh toán");
                TempData["Error"] = "Có lỗi xảy ra khi xử lý đặt vé. Vui lòng thử lại.";

                // Reload data for view
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

                var choNgoi = await _context.ChoNgois
                    .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

                model.ChuyenXe = chuyenXe!;
                model.ChoNgoi = choNgoi!;
                model.TongTien = bookingStep.TongTien;
                model.ThanhTien = bookingStep.TongTien;
                return View(model);
            }

            // Sau khi tạo booking thành công, xử lý thanh toán
            if (model.PhuongThucThanhToan == PhuongThucThanhToan.TaiQuay)
            {
                // Thanh toán tại quầy - redirect trực tiếp đến PaymentSuccess
                TempData["Success"] = "Đặt vé thành công! Vui lòng thanh toán tại quầy khi lên xe.";
                return RedirectToAction("PaymentSuccess", new { sessionId });
            }
            else
            {
                // Thanh toán online - redirect đến ProcessPayment
                var thanhToanId = await _context.ThanhToans
                    .Where(t => t.MaGiaoDich == bookingStep.MaGiaoDich)
                    .Select(t => t.ThanhToanId)
                    .FirstOrDefaultAsync();

                return RedirectToAction("ProcessPayment", new { sessionId, paymentId = thanhToanId });
            }
        }

        // GET: Booking/ProcessPayment - Xử lý thanh toán qua gateway
        public async Task<IActionResult> ProcessPayment(string sessionId, int paymentId)
        {
            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null)
            {
                TempData["Error"] = "Phiên đặt vé không hợp lệ";
                return RedirectToAction("Search");
            }

            var thanhToan = await _context.ThanhToans
                .Include(t => t.Ve)
                .ThenInclude(v => v.ChuyenXe)
                .FirstOrDefaultAsync(t => t.ThanhToanId == paymentId);

            if (thanhToan == null)
            {
                TempData["Error"] = "Không tìm thấy giao dịch thanh toán";
                return RedirectToAction("Search");
            }

            try
            {
                // Tạo payment request
                var paymentRequest = new PaymentRequest
                {
                    TicketId = thanhToan.VeId,
                    Method = thanhToan.PhuongThucThanhToan,
                    Amount = thanhToan.SoTien,
                    ReturnUrl = Url.Action("PaymentCallback", "Booking", new { sessionId }, Request.Scheme),
                    CancelUrl = Url.Action("PaymentCancel", "Booking", new { sessionId }, Request.Scheme),
                    OrderInfo = $"Thanh toan ve {thanhToan.Ve?.MaVe}",
                    CustomerName = bookingStep.TenKhach,
                    CustomerPhone = bookingStep.SoDienThoai,
                    CustomerEmail = bookingStep.Email
                };

                var paymentResponse = await _paymentService.CreatePaymentAsync(paymentRequest);
                _logger.LogInformation($"PaymentService response: Success={paymentResponse.Success}, Message={paymentResponse.Message}");

                if (paymentResponse.Success)
                {
                    // Xử lý đặc biệt cho thanh toán tại quầy
                    if (thanhToan.PhuongThucThanhToan == PhuongThucThanhToan.TaiQuay)
                    {
                        _logger.LogInformation("Processing TaiQuay payment - updating status to success");
                        // Cập nhật trạng thái thanh toán thành công ngay lập tức
                        thanhToan.TrangThai = TrangThaiThanhToan.ThanhCong;
                        thanhToan.ThoiGianThanhToan = DateTime.Now;
                        thanhToan.MaPhanHoi = thanhToan.MaGiaoDich;
                        thanhToan.ThongTinPhanHoi = "Thanh toán tại quầy";

                        // Cập nhật trạng thái vé
                        if (thanhToan.Ve != null)
                        {
                            thanhToan.Ve.VeTrangThai = TrangThaiVe.DaThanhToan;
                        }

                        await _context.SaveChangesAsync();
                        _logger.LogInformation("TaiQuay payment processed successfully - redirecting to PaymentSuccess");

                        TempData["Success"] = "Đặt vé thành công! Vui lòng thanh toán tại quầy khi lên xe.";
                        return RedirectToAction("PaymentSuccess", new { sessionId });
                    }
                    else if (!string.IsNullOrEmpty(paymentResponse.PaymentUrl))
                    {
                        // Cập nhật trạng thái thanh toán cho gateway
                        thanhToan.TrangThai = TrangThaiThanhToan.DangXuLy;
                        await _context.SaveChangesAsync();

                        // Chuyển hướng đến gateway thanh toán
                        return Redirect(paymentResponse.PaymentUrl);
                    }
                    else
                    {
                        TempData["Error"] = "Không thể tạo liên kết thanh toán";
                        return RedirectToAction("Payment", new { sessionId });
                    }
                }
                else
                {
                    TempData["Error"] = paymentResponse.Message;
                    return RedirectToAction("Payment", new { sessionId });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi xử lý thanh toán");
                TempData["Error"] = "Có lỗi xảy ra khi xử lý thanh toán";
                return RedirectToAction("Payment", new { sessionId });
            }
        }

        // GET: Booking/PaymentCallback - Xử lý callback từ gateway
        public async Task<IActionResult> PaymentCallback(string sessionId)
        {
            _logger.LogInformation($"PaymentCallback called with sessionId: {sessionId}");

            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null)
            {
                _logger.LogWarning($"Invalid booking session: {sessionId}");
                return RedirectToAction("PaymentFailure", new {
                    sessionId,
                    errorMessage = "Phiên đặt vé không hợp lệ hoặc đã hết hạn",
                    errorCode = "INVALID_SESSION"
                });
            }

            try
            {
                // Lấy parameters từ query string
                var parameters = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
                _logger.LogInformation($"Payment callback parameters: {string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}");

                // Validate required parameters
                if (!ValidateCallbackParameters(parameters, out string validationError))
                {
                    _logger.LogWarning($"Invalid callback parameters: {validationError}");
                    return RedirectToAction("PaymentFailure", new {
                        sessionId,
                        errorMessage = validationError,
                        errorCode = "INVALID_PARAMETERS"
                    });
                }

                // Kiểm tra nếu là demo payment
                bool isDemo = parameters.ContainsKey("demo") && parameters["demo"] == "true";
                bool paymentSuccess = false;
                string? transactionId = null;
                string? errorMessage = null;

                if (isDemo)
                {
                    // Xử lý demo payment
                    paymentSuccess = parameters.ContainsKey("status") && parameters["status"] == "success";
                    transactionId = parameters.GetValueOrDefault("transaction_id");
                    errorMessage = parameters.GetValueOrDefault("error");
                    _logger.LogInformation($"Demo payment result: success={paymentSuccess}, transactionId={transactionId}");
                }
                else
                {
                    // Xử lý payment thật qua service
                    var paymentResponse = await _paymentService.ProcessPaymentCallbackAsync(
                        bookingStep.MaGiaoDich ?? "", parameters);
                    paymentSuccess = paymentResponse.Success;
                    transactionId = paymentResponse.TransactionId;
                    errorMessage = paymentResponse.Message;
                    _logger.LogInformation($"Real payment result: success={paymentSuccess}, transactionId={transactionId}, message={errorMessage}");
                }

                if (paymentSuccess)
                {
                    // Cập nhật trạng thái thanh toán
                    var thanhToan = await _context.ThanhToans
                        .Include(t => t.Ve)
                        .FirstOrDefaultAsync(t => t.MaGiaoDich == bookingStep.MaGiaoDich);

                    if (thanhToan == null)
                    {
                        _logger.LogError($"Payment record not found for transaction: {bookingStep.MaGiaoDich}");
                        return RedirectToAction("PaymentFailure", new {
                            sessionId,
                            errorMessage = "Không tìm thấy thông tin giao dịch",
                            errorCode = "TRANSACTION_NOT_FOUND",
                            transactionId = bookingStep.MaGiaoDich
                        });
                    }

                    // Kiểm tra trạng thái hiện tại để tránh xử lý trùng lặp
                    if (thanhToan.TrangThai == TrangThaiThanhToan.ThanhCong)
                    {
                        _logger.LogInformation($"Payment already processed successfully: {bookingStep.MaGiaoDich}");
                        TempData["Success"] = "Thanh toán đã được xử lý thành công trước đó.";
                        return RedirectToAction("PaymentSuccess", new { sessionId });
                    }

                    // Cập nhật trạng thái thanh toán
                    thanhToan.TrangThai = TrangThaiThanhToan.ThanhCong;
                    thanhToan.ThoiGianThanhToan = DateTime.Now;
                    thanhToan.MaPhanHoi = transactionId ?? bookingStep.MaGiaoDich;
                    thanhToan.ThongTinPhanHoi = isDemo ? "Thanh toán demo thành công" : "Thanh toán thành công";

                    // ✅ ĐỒNG BỘ: Cập nhật trạng thái vé khi thanh toán thành công
                    if (thanhToan.Ve != null)
                    {
                        thanhToan.Ve.TrangThai = TrangThaiVe.DaThanhToan;
                        _logger.LogInformation($"Updated ticket status to paid: {thanhToan.Ve.MaVe}");
                    }

                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"Payment processed successfully: {bookingStep.MaGiaoDich}");

                    TempData["Success"] = "Thanh toán thành công! Vé của bạn đã được xác nhận.";
                    return RedirectToAction("PaymentSuccess", new { sessionId });
                }
                else
                {
                    // Cập nhật trạng thái thanh toán thất bại
                    var thanhToan = await _context.ThanhToans
                        .FirstOrDefaultAsync(t => t.MaGiaoDich == bookingStep.MaGiaoDich);

                    if (thanhToan != null)
                    {
                        thanhToan.TrangThai = TrangThaiThanhToan.ThatBai;
                        thanhToan.ThoiGianThanhToan = DateTime.Now;
                        thanhToan.MaPhanHoi = transactionId ?? bookingStep.MaGiaoDich;
                        thanhToan.ThongTinPhanHoi = errorMessage ?? "Thanh toán thất bại";
                        await _context.SaveChangesAsync();
                        _logger.LogWarning($"Payment failed: {bookingStep.MaGiaoDich}, reason: {errorMessage}");
                    }

                    string finalErrorCode = parameters.GetValueOrDefault("error_code") ?? "PAYMENT_FAILED";
                    string finalErrorMessage = errorMessage ?? "Thanh toán thất bại";

                    return RedirectToAction("PaymentFailure", new {
                        sessionId,
                        errorMessage = finalErrorMessage,
                        errorCode = finalErrorCode,
                        transactionId = transactionId ?? bookingStep.MaGiaoDich
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error processing payment callback for session: {sessionId}");
                return RedirectToAction("PaymentFailure", new {
                    sessionId,
                    errorMessage = "Có lỗi hệ thống khi xử lý thanh toán. Vui lòng liên hệ hỗ trợ.",
                    errorCode = "SYSTEM_ERROR",
                    transactionId = bookingStep?.MaGiaoDich
                });
            }
        }

        // Helper method để validate callback parameters
        private bool ValidateCallbackParameters(Dictionary<string, string> parameters, out string errorMessage)
        {
            errorMessage = string.Empty;

            // Kiểm tra các parameters cơ bản
            if (!parameters.ContainsKey("status") && !parameters.ContainsKey("vnp_ResponseCode") &&
                !parameters.ContainsKey("resultCode") && !parameters.ContainsKey("return_code"))
            {
                errorMessage = "Thiếu thông tin trạng thái thanh toán";
                return false;
            }

            // Validate demo payment
            if (parameters.ContainsKey("demo") && parameters["demo"] == "true")
            {
                if (!parameters.ContainsKey("status"))
                {
                    errorMessage = "Thiếu thông tin trạng thái demo payment";
                    return false;
                }
                return true;
            }

            // Validate VNPay response
            if (parameters.ContainsKey("vnp_ResponseCode"))
            {
                if (!parameters.ContainsKey("vnp_TxnRef") || !parameters.ContainsKey("vnp_Amount"))
                {
                    errorMessage = "Thiếu thông tin giao dịch VNPay";
                    return false;
                }
                return true;
            }

            // Validate MoMo response
            if (parameters.ContainsKey("resultCode"))
            {
                if (!parameters.ContainsKey("orderId") || !parameters.ContainsKey("amount"))
                {
                    errorMessage = "Thiếu thông tin giao dịch MoMo";
                    return false;
                }
                return true;
            }

            // Validate ZaloPay response
            if (parameters.ContainsKey("return_code"))
            {
                if (!parameters.ContainsKey("app_trans_id") || !parameters.ContainsKey("amount"))
                {
                    errorMessage = "Thiếu thông tin giao dịch ZaloPay";
                    return false;
                }
                return true;
            }

            return true;
        }

        // GET: Booking/PaymentCancel - Xử lý khi hủy thanh toán
        public IActionResult PaymentCancel(string sessionId)
        {
            return RedirectToAction("PaymentFailure", new {
                sessionId,
                errorMessage = "Bạn đã hủy thanh toán",
                errorCode = "PAYMENT_CANCELLED"
            });
        }

        // GET: Booking/DemoPayment - Trang demo thanh toán
        public IActionResult DemoPayment(string method, string transactionId, decimal amount, string orderInfo, string returnUrl, string cancelUrl)
        {
            ViewBag.PaymentMethod = method;
            ViewBag.TransactionId = transactionId;
            ViewBag.Amount = amount;
            ViewBag.OrderInfo = orderInfo;
            ViewBag.ReturnUrl = returnUrl;
            ViewBag.CancelUrl = cancelUrl;

            return View();
        }

        // GET: Booking/Confirmation - Xác nhận đặt vé thành công
        public async Task<IActionResult> Confirmation(string sessionId)
        {
            var bookingStep = GetBookingFromSession(sessionId);
            if (bookingStep == null || string.IsNullOrEmpty(bookingStep.MaVe))
            {
                TempData["Error"] = "Không tìm thấy thông tin đặt vé";
                return RedirectToAction("Search");
            }

            // Lấy thông tin chuyến xe và ghế từ booking step
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

            var choNgoi = await _context.ChoNgois
                .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

            if (chuyenXe == null || choNgoi == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin chuyến xe hoặc ghế";
                return RedirectToAction("Search");
            }

            var thanhToan = await _context.ThanhToans
                .FirstOrDefaultAsync(t => t.MaGiaoDich == bookingStep.MaGiaoDich);

            // Gửi email và SMS xác nhận
            bool emailSent = false;
            bool smsSent = false;

            if (!string.IsNullOrEmpty(bookingStep.Email))
            {
                try
                {
                    var tripInfo = $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}";
                    var seatInfo = choNgoi.SoGhe ?? "Chưa chọn chỗ cụ thể";

                    emailSent = await _emailService.SendTicketConfirmationAsync(
                        bookingStep.Email, bookingStep.TenKhach, bookingStep.MaVe, tripInfo, seatInfo,
                        bookingStep.TongTien, chuyenXe.NgayKhoiHanh);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi email xác nhận");
                }
            }

            if (!string.IsNullOrEmpty(bookingStep.SoDienThoai))
            {
                try
                {
                    var tripInfo = $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}";
                    var seatInfo = choNgoi.SoGhe ?? "Chưa chọn chỗ cụ thể";

                    smsSent = await _smsService.SendTicketConfirmationSMSAsync(
                        bookingStep.SoDienThoai, bookingStep.TenKhach, bookingStep.MaVe, tripInfo, seatInfo,
                        bookingStep.TongTien, chuyenXe.NgayKhoiHanh);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi SMS xác nhận");
                }
            }

            var viewModel = new BookingConfirmationViewModel
            {
                MaVe = bookingStep.MaVe,
                TenKhach = bookingStep.TenKhach,
                SoDienThoai = bookingStep.SoDienThoai,
                Email = bookingStep.Email,
                GiaVe = bookingStep.TongTien,
                ChuyenXe = chuyenXe,
                ChoNgoi = choNgoi,
                ThanhToan = thanhToan,
                EmailSent = emailSent,
                SMSSent = smsSent
            };

            // Xóa session booking
            HttpContext.Session.Remove($"Booking_{sessionId}");

            return View(viewModel);
        }

        // GET: Booking/PaymentSuccess - Trang thanh toán thành công
        public async Task<IActionResult> PaymentSuccess(string sessionId)
        {
            _logger.LogInformation($"PaymentSuccess called with sessionId: {sessionId}");

            var bookingStep = GetBookingFromSession(sessionId);
            _logger.LogInformation($"BookingStep found: {bookingStep != null}, MaVe: {bookingStep?.MaVe}");

            // Nếu không tìm thấy session, thử tìm từ database bằng VeId
            if (bookingStep == null)
            {
                _logger.LogWarning($"BookingStep not found for sessionId: {sessionId}, trying to find from database");

                // Thử parse sessionId như VeId
                if (int.TryParse(sessionId, out int veId))
                {
                    var ve = await _context.Ves
                        .Include(v => v.ChuyenXe)
                        .Include(v => v.ChoNgoi)
                        .Include(v => v.ThanhToans)
                        .FirstOrDefaultAsync(v => v.VeId == veId);

                    if (ve != null)
                    {
                        _logger.LogInformation($"Found ticket from database: VeId={ve.VeId}, MaVe={ve.MaVe}");
                        return await ShowPaymentSuccessFromDatabase(ve);
                    }
                }

                _logger.LogWarning($"Cannot find ticket information for sessionId: {sessionId}");
                TempData["Error"] = "Không tìm thấy thông tin đặt vé";
                return RedirectToAction("Search");
            }

            if (string.IsNullOrEmpty(bookingStep.MaVe))
            {
                _logger.LogWarning($"MaVe is empty for sessionId: {sessionId}");
                TempData["Error"] = "Mã vé không hợp lệ";
                return RedirectToAction("Search");
            }

            // Lấy thông tin chuyến xe và ghế từ booking step
            var chuyenXe = await _context.ChuyenXes
                .Include(c => c.Xe)
                .Include(c => c.TuyenDuong)
                .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

            var choNgoi = await _context.ChoNgois
                .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

            // Lấy thông tin thanh toán
            var thanhToan = await _context.ThanhToans
                .FirstOrDefaultAsync(t => t.MaGiaoDich == bookingStep.MaGiaoDich);

            if (chuyenXe == null)
            {
                TempData["Error"] = "Không tìm thấy thông tin chuyến xe";
                return RedirectToAction("Search");
            }

            // Gửi email và SMS xác nhận
            bool emailSent = false;
            bool smsSent = false;

            // Generate QR code data
            var qrCodeData = GenerateQRCodeData(bookingStep, chuyenXe, choNgoi);

            if (!string.IsNullOrEmpty(bookingStep.Email))
            {
                try
                {
                    var tripInfo = $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}";
                    var seatInfo = choNgoi?.SoGhe ?? "Chưa chọn chỗ cụ thể";

                    emailSent = await _emailService.SendTicketConfirmationWithQRAsync(
                        bookingStep.Email, bookingStep.TenKhach, bookingStep.MaVe, tripInfo, seatInfo,
                        bookingStep.TongTien, chuyenXe.NgayKhoiHanh, qrCodeData);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi email xác nhận");
                }
            }

            if (!string.IsNullOrEmpty(bookingStep.SoDienThoai))
            {
                try
                {
                    var tripInfo = $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}";
                    var seatInfo = choNgoi?.SoGhe ?? "Chưa chọn chỗ cụ thể";

                    smsSent = await _smsService.SendTicketConfirmationSMSAsync(
                        bookingStep.SoDienThoai, bookingStep.TenKhach, bookingStep.MaVe, tripInfo, seatInfo,
                        bookingStep.TongTien, chuyenXe.NgayKhoiHanh);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi khi gửi SMS xác nhận");
                }
            }

            // Debug logging để kiểm tra giá trị
            _logger.LogInformation($"BookingStep values - TongTien: {bookingStep.TongTien}, ThanhTien: {bookingStep.ThanhTien}, GiamGia: {bookingStep.GiamGia}");
            _logger.LogInformation($"ThanhToan values - SoTien: {thanhToan?.SoTien}");

            // Sử dụng số tiền từ ThanhToan nếu có, nếu không thì dùng ThanhTien từ session
            var actualAmount = thanhToan?.SoTien ?? bookingStep.ThanhTien;
            if (actualAmount == 0)
            {
                actualAmount = bookingStep.TongTien; // Fallback về giá gốc nếu cả hai đều 0
            }

            _logger.LogInformation($"Using actualAmount: {actualAmount}");

            var viewModel = new BookingConfirmationViewModel
            {
                MaVe = bookingStep.MaVe,
                TenKhach = bookingStep.TenKhach,
                SoDienThoai = bookingStep.SoDienThoai,
                Email = bookingStep.Email,
                GiaVe = actualAmount, // Sử dụng số tiền thực tế
                ChuyenXe = chuyenXe,
                ChoNgoi = choNgoi,
                ThanhToan = thanhToan,
                EmailSent = emailSent,
                SMSSent = smsSent,
                QRCodeData = qrCodeData,
                // Thêm thông tin giảm giá để hiển thị chi tiết
                TongTienGoc = bookingStep.TongTien,
                GiamGia = bookingStep.GiamGia,
                MaKhuyenMai = bookingStep.MaKhuyenMai
            };

            // Xóa session booking
            HttpContext.Session.Remove($"Booking_{sessionId}");

            return View(viewModel);
        }

        // GET: Booking/PaymentFailure - Trang thanh toán thất bại
        public async Task<IActionResult> PaymentFailure(string sessionId, string? errorMessage = null,
            string? errorCode = null, string? transactionId = null)
        {
            var bookingStep = GetBookingFromSession(sessionId);

            var viewModel = new PaymentFailureViewModel
            {
                ErrorCode = errorCode ?? "PAYMENT_FAILED",
                ErrorMessage = errorMessage ?? "Giao dịch thanh toán không thành công",
                TransactionId = transactionId,
                SessionId = sessionId
            };

            // Nếu có thông tin booking, thêm vào viewModel
            if (bookingStep != null)
            {
                var chuyenXe = await _context.ChuyenXes
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == bookingStep.ChuyenXeId);

                var choNgoi = await _context.ChoNgois
                    .FirstOrDefaultAsync(c => c.ChoNgoiId == bookingStep.ChoNgoiId);

                if (chuyenXe != null)
                {
                    viewModel.BookingInfo = new BookingFailureInfo
                    {
                        MaVe = bookingStep.MaVe ?? "N/A",
                        TenKhach = bookingStep.TenKhach,
                        SoDienThoai = bookingStep.SoDienThoai,
                        DiemDi = chuyenXe.DiemDiDisplay,
                        DiemDen = chuyenXe.DiemDenDisplay,
                        NgayKhoiHanh = chuyenXe.NgayKhoiHanh,
                        SoGhe = choNgoi?.SoGhe,
                        GiaVe = bookingStep.TongTien
                    };
                }

                viewModel.Amount = bookingStep.TongTien;
                viewModel.PaymentMethod = bookingStep.PhuongThucThanhToan.GetDisplayName();
            }

            return View(viewModel);
        }

        // Helper method để hiển thị trang thành công từ database khi không có session
        private async Task<IActionResult> ShowPaymentSuccessFromDatabase(Ve ve)
        {
            try
            {
                var chuyenXe = ve.ChuyenXe ?? await _context.ChuyenXes
                    .Include(c => c.Xe)
                    .Include(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(c => c.ChuyenXeId == ve.ChuyenXeId);

                var choNgoi = ve.ChoNgoi ?? await _context.ChoNgois
                    .FirstOrDefaultAsync(c => c.ChoNgoiId == ve.ChoNgoiId);

                var thanhToan = ve.ThanhToans?.FirstOrDefault() ?? await _context.ThanhToans
                    .FirstOrDefaultAsync(t => t.VeId == ve.VeId);

                if (chuyenXe == null)
                {
                    _logger.LogWarning($"ChuyenXe not found for VeId: {ve.VeId}");
                    TempData["Error"] = "Không tìm thấy thông tin chuyến xe";
                    return RedirectToAction("Search");
                }

                // Generate QR code data
                var qrCodeData = _qrCodeService.GenerateTicketQRData(
                    ve.MaVe,
                    ve.TenKhach,
                    ve.SoDienThoai,
                    chuyenXe.ChuyenXeId,
                    choNgoi?.SoGhe ?? "",
                    chuyenXe.NgayKhoiHanh,
                    $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}",
                    ve.GiaVe
                );

                // Lấy số tiền thực tế đã thanh toán từ ThanhToan hoặc Ve
                var actualAmount = thanhToan?.SoTien ?? ve.GiaVe;

                _logger.LogInformation($"Payment amounts - ve.GiaVe: {ve.GiaVe}, thanhToan.SoTien: {thanhToan?.SoTien}, using: {actualAmount}");

                var viewModel = new BookingConfirmationViewModel
                {
                    MaVe = ve.MaVe,
                    TenKhach = ve.TenKhach,
                    SoDienThoai = ve.SoDienThoai,
                    Email = ve.Email,
                    GiaVe = actualAmount, // Sử dụng số tiền thực tế đã thanh toán
                    ChuyenXe = chuyenXe,
                    ChoNgoi = choNgoi,
                    ThanhToan = thanhToan,
                    EmailSent = false, // Không gửi lại email/SMS
                    SMSSent = false,
                    QRCodeData = qrCodeData,
                    TongTienGoc = actualAmount, // Sử dụng số tiền thực tế
                    GiamGia = 0,
                    MaKhuyenMai = null
                };

                _logger.LogInformation($"Showing payment success from database for VeId: {ve.VeId}, MaVe: {ve.MaVe}");
                return View("PaymentSuccess", viewModel);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error showing payment success from database for VeId: {ve.VeId}");
                TempData["Error"] = "Có lỗi xảy ra khi hiển thị thông tin vé";
                return RedirectToAction("Search");
            }
        }

        // Helper method để tạo QR code data
        private string GenerateQRCodeData(BookingStepViewModel bookingStep, ChuyenXe chuyenXe, ChoNgoi? choNgoi)
        {
            return _qrCodeService.GenerateTicketQRData(
                bookingStep.MaVe ?? "",
                bookingStep.TenKhach,
                bookingStep.SoDienThoai,
                chuyenXe.ChuyenXeId,
                choNgoi?.SoGhe ?? "",
                chuyenXe.NgayKhoiHanh,
                $"{chuyenXe.DiemDiDisplay} - {chuyenXe.DiemDenDisplay}",
                bookingStep.TongTien
            );
        }

        // GET: Booking/QRCode - Tạo QR code image cho vé
        public async Task<IActionResult> QRCode(string ticketCode)
        {
            if (string.IsNullOrEmpty(ticketCode))
            {
                return BadRequest("Mã vé không hợp lệ");
            }

            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Include(v => v.ChoNgoi)
                    .FirstOrDefaultAsync(v => v.MaVe == ticketCode);

                if (ve == null)
                {
                    return NotFound("Không tìm thấy vé");
                }

                var qrCodeData = $"TICKET:{ve.MaVe}|PASSENGER:{ve.TenKhach}|PHONE:{ve.SoDienThoai}|TRIP:{ve.ChuyenXe?.DiemDi}-{ve.ChuyenXe?.DiemDen}|DATE:{ve.ChuyenXe?.NgayKhoiHanh:yyyy-MM-dd HH:mm}|SEAT:{ve.ChoNgoi?.SoGhe}";

                // Sinh QR code bằng QRCoder
                using (var qrGenerator = new QRCodeGenerator())
                using (var qrData = qrGenerator.CreateQrCode(qrCodeData, QRCodeGenerator.ECCLevel.Q))
                {
                    var pngQrCode = new PngByteQRCode(qrData);
                    var qrCodeBytes = pngQrCode.GetGraphic(20);
                    return File(qrCodeBytes, "image/png");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating QR code for ticket {TicketCode}", ticketCode);
                return StatusCode(500, "Lỗi tạo QR code");
            }
        }

        // POST: Booking/ApplyPromoCode - Áp dụng mã khuyến mãi
        [HttpPost]
        public async Task<IActionResult> ApplyPromoCode(string sessionId, string promoCode)
        {
            try
            {
                var bookingStep = GetBookingFromSession(sessionId);
                if (bookingStep == null)
                {
                    return Json(new { success = false, message = "Phiên đặt vé không hợp lệ" });
                }

                if (string.IsNullOrWhiteSpace(promoCode))
                {
                    return Json(new { success = false, message = "Vui lòng nhập mã khuyến mãi" });
                }

                // Tìm mã khuyến mãi
                var khuyenMai = await _context.KhuyenMais
                    .FirstOrDefaultAsync(k => k.MaKhuyenMai == promoCode.Trim().ToUpper());

                if (khuyenMai == null)
                {
                    return Json(new { success = false, message = "Mã khuyến mãi không tồn tại" });
                }

                // Kiểm tra tính hợp lệ của mã khuyến mãi
                var validationResult = ValidatePromoCode(khuyenMai);
                if (!validationResult.IsValid)
                {
                    return Json(new { success = false, message = validationResult.ErrorMessage });
                }

                // Tính toán giảm giá
                var originalAmount = bookingStep.TongTien;
                var discountAmount = CalculateDiscount(khuyenMai, originalAmount);
                var finalAmount = originalAmount - discountAmount;

                // Cập nhật thông tin vào booking step
                bookingStep.MaKhuyenMai = khuyenMai.MaKhuyenMai;
                bookingStep.GiamGia = discountAmount;
                bookingStep.ThanhTien = finalAmount;
                SaveBookingToSession(sessionId, bookingStep);

                return Json(new
                {
                    success = true,
                    message = "Áp dụng mã khuyến mãi thành công",
                    promoCode = khuyenMai.MaKhuyenMai,
                    promoName = khuyenMai.TenKhuyenMai,
                    discountAmount = discountAmount,
                    originalAmount = originalAmount,
                    finalAmount = finalAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error applying promo code: {promoCode}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi áp dụng mã khuyến mãi" });
            }
        }

        // POST: Booking/RemovePromoCode - Hủy mã khuyến mãi
        [HttpPost]
        public IActionResult RemovePromoCode(string sessionId)
        {
            try
            {
                var bookingStep = GetBookingFromSession(sessionId);
                if (bookingStep == null)
                {
                    return Json(new { success = false, message = "Phiên đặt vé không hợp lệ" });
                }

                // Reset về giá gốc
                bookingStep.MaKhuyenMai = null;
                bookingStep.GiamGia = 0;
                bookingStep.ThanhTien = bookingStep.TongTien;
                SaveBookingToSession(sessionId, bookingStep);

                return Json(new
                {
                    success = true,
                    message = "Đã hủy mã khuyến mãi",
                    finalAmount = bookingStep.TongTien
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing promo code");
                return Json(new { success = false, message = "Có lỗi xảy ra khi hủy mã khuyến mãi" });
            }
        }

        // Helper method để validate mã khuyến mãi
        private (bool IsValid, string ErrorMessage) ValidatePromoCode(KhuyenMai khuyenMai)
        {
            var now = DateTime.Now;

            if (!khuyenMai.TrangThai)
                return (false, "Mã khuyến mãi đã bị vô hiệu hóa");

            if (now < khuyenMai.NgayBatDau)
                return (false, $"Mã khuyến mãi chưa có hiệu lực (từ {khuyenMai.NgayBatDau:dd/MM/yyyy})");

            if (now > khuyenMai.NgayKetThuc)
                return (false, $"Mã khuyến mãi đã hết hạn (đến {khuyenMai.NgayKetThuc:dd/MM/yyyy})");

            if (khuyenMai.SoLuongDaSuDung >= khuyenMai.SoLuongToiDa)
                return (false, "Mã khuyến mãi đã hết lượt sử dụng");

            return (true, string.Empty);
        }

        // Helper method để tính toán giảm giá
        private decimal CalculateDiscount(KhuyenMai khuyenMai, decimal originalAmount)
        {
            decimal discountAmount = 0;

            if (khuyenMai.LoaiKhuyenMai == LoaiKhuyenMai.GiamPhanTram)
            {
                discountAmount = originalAmount * khuyenMai.GiaTri / 100;
            }
            else if (khuyenMai.LoaiKhuyenMai == LoaiKhuyenMai.GiamSoTien)
            {
                discountAmount = khuyenMai.GiaTri;
            }

            if (khuyenMai.GiamToiDa > 0 && discountAmount > khuyenMai.GiamToiDa)
                discountAmount = khuyenMai.GiamToiDa;

            if (discountAmount > originalAmount)
                discountAmount = originalAmount;

            return discountAmount;
        }

        // POST: Booking/CheckCancellation - Kiểm tra điều kiện hủy vé
        [HttpPost]
        public async Task<IActionResult> CheckCancellation(int ticketId)
        {
            try
            {
                var userId = HttpContext.Session.GetInt32("UserId");
                var result = await _cancellationService.CanCancelTicketAsync(ticketId, userId);

                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    data = result.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking cancellation for ticket {ticketId}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi kiểm tra điều kiện hủy vé" });
            }
        }

        // POST: Booking/CancelTicket - Hủy vé
        [HttpPost]
        public async Task<IActionResult> CancelTicket(int ticketId, string reason)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(reason))
                {
                    return Json(new { success = false, message = "Vui lòng nhập lý do hủy vé" });
                }

                var userId = HttpContext.Session.GetInt32("UserId");
                var result = await _cancellationService.CancelTicketAsync(ticketId, reason, userId);

                if (result.Success)
                {
                    TempData["Success"] = result.Message;
                }

                return Json(new
                {
                    success = result.Success,
                    message = result.Message,
                    data = result.Data
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error cancelling ticket {ticketId}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi hủy vé. Vui lòng thử lại." });
            }
        }

        // GET: Booking/CancellationPolicy - Trang chính sách hủy vé
        public IActionResult CancellationPolicy()
        {
            return View();
        }

        // GET: Booking/TestBookingFlow - Trang test quy trình đặt vé (chỉ cho development)
        public IActionResult TestBookingFlow()
        {
            // Chỉ cho phép trong môi trường development
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            if (environment != "Development")
            {
                return NotFound();
            }

            return View();
        }

        // ADMIN: Danh sách đơn đặt vé với filter nâng cao
        public async Task<IActionResult> AdminBookingList(DateTime? ngayDi, int? chuyenXeId, TrangThaiVe? trangThaiVe, string? tenKhach,
            DateTime? tuNgay, DateTime? denNgay, string? soDienThoai, string? email, string? maVe,
            decimal? giaVeMin, decimal? giaVeMax, bool? trangThaiThanhToan)
        {
            var query = _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(cx => cx.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .AsQueryable();

            // Basic filters
            if (ngayDi.HasValue)
            {
                query = query.Where(v => v.ChuyenXe != null && v.ChuyenXe.NgayKhoiHanh.Date == ngayDi.Value.Date);
            }
            if (chuyenXeId.HasValue)
            {
                query = query.Where(v => v.ChuyenXeId == chuyenXeId.Value);
            }
            if (trangThaiVe.HasValue)
            {
                query = query.Where(v => v.VeTrangThai == trangThaiVe.Value);
            }
            if (!string.IsNullOrEmpty(tenKhach))
            {
                query = query.Where(v => v.TenKhach.Contains(tenKhach));
            }

            // Advanced filters
            if (tuNgay.HasValue)
            {
                query = query.Where(v => v.NgayDat.Date >= tuNgay.Value.Date);
            }
            if (denNgay.HasValue)
            {
                query = query.Where(v => v.NgayDat.Date <= denNgay.Value.Date);
            }
            if (!string.IsNullOrEmpty(soDienThoai))
            {
                query = query.Where(v => v.SoDienThoai.Contains(soDienThoai));
            }
            if (!string.IsNullOrEmpty(email))
            {
                query = query.Where(v => v.Email.Contains(email));
            }
            if (!string.IsNullOrEmpty(maVe))
            {
                query = query.Where(v => v.MaVe.Contains(maVe));
            }
            if (giaVeMin.HasValue)
            {
                query = query.Where(v => v.GiaVe >= giaVeMin.Value);
            }
            if (giaVeMax.HasValue)
            {
                query = query.Where(v => v.GiaVe <= giaVeMax.Value);
            }
            if (trangThaiThanhToan.HasValue)
            {
                if (trangThaiThanhToan.Value)
                {
                    query = query.Where(v => v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong));
                }
                else
                {
                    query = query.Where(v => !v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong));
                }
            }

            // Truyền filter cho ViewBag để giữ giá trị khi render lại
            ViewBag.SelectedChuyenXeId = chuyenXeId?.ToString() ?? "";
            ViewBag.SelectedNgayDi = ngayDi?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.SelectedTrangThaiVe = trangThaiVe.HasValue ? ((int)trangThaiVe.Value).ToString() : "";
            ViewBag.SelectedTenKhach = tenKhach ?? "";

            // Store advanced filter values for view
            ViewBag.TuNgayFilter = tuNgay?.ToString("yyyy-MM-dd");
            ViewBag.DenNgayFilter = denNgay?.ToString("yyyy-MM-dd");
            ViewBag.SoDienThoaiFilter = soDienThoai;
            ViewBag.EmailFilter = email;
            ViewBag.MaVeFilter = maVe;
            ViewBag.GiaVeMinFilter = giaVeMin;
            ViewBag.GiaVeMaxFilter = giaVeMax;
            ViewBag.TrangThaiThanhToanFilter = trangThaiThanhToan?.ToString().ToLower();

            var list = await query.OrderByDescending(v => v.NgayDat)
                .Select(v => new {
                    v.VeId,
                    v.MaVe,
                    v.TenKhach,
                    v.SoDienThoai,
                    v.Email,
                    ChuyenXe = v.ChuyenXe != null ? (v.ChuyenXe.TuyenDuong != null ? v.ChuyenXe.TuyenDuong.TenTuyen : (v.ChuyenXe.DiemDi + " - " + v.ChuyenXe.DiemDen)) + " (" + v.ChuyenXe.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm") + ")" : "",
                    Ghe = v.ChoNgoi != null ? v.ChoNgoi.SoGhe : "",
                    TrangThai = v.VeTrangThai,
                    NgayDat = v.NgayDat
                })
                .ToListAsync();

            ViewBag.ChuyenXeList = await _context.ChuyenXes.Include(cx => cx.TuyenDuong).ToListAsync();
            ViewBag.TrangThaiVeList = Enum.GetValues(typeof(TrangThaiVe)).Cast<TrangThaiVe>().ToList();

            // Set selected filter values for view
            ViewBag.SelectedChuyenXeId = chuyenXeId?.ToString() ?? "";
            ViewBag.SelectedNgayDi = ngayDi?.ToString("yyyy-MM-dd") ?? "";
            ViewBag.SelectedTrangThaiVe = trangThaiVe?.ToString() ?? "";
            ViewBag.SelectedTenKhach = tenKhach ?? "";

            return View(list);
        }

        // ADMIN: Xem chi tiết đặt vé
        public async Task<IActionResult> AdminBookingDetail(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(cx => cx.TuyenDuong)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .FirstOrDefaultAsync(v => v.VeId == id);
            if (ve == null)
            {
                return NotFound();
            }

            // QR code: chỉ truyền mã, view sẽ render
            var qrCodeData = ve.MaVe;

            // Lấy phương thức thanh toán gần nhất (nếu có)
            var phuongThucThanhToan = ve.ThanhToans?.OrderByDescending(t => t.NgayThanhToan).FirstOrDefault()?.PhuongThucThanhToan;
            var thoiGianThanhToan = ve.ThanhToans?.OrderByDescending(t => t.NgayThanhToan).FirstOrDefault()?.NgayThanhToan;

            var detail = new
            {
                ve.VeId,
                ve.MaVe,
                ve.TenKhach,
                ve.SoDienThoai,
                ve.Email,
                ve.GiaVe,
                TongTien = ve.GiaVe, // Alias for compatibility
                ve.GhiChu,
                ve.NgayDat,
                ve.VeTrangThai,
                ve.LyDoHuy,
                ve.ChuyenXe,
                ve.ChoNgoi,
                DanhSachGhe = ve.ChoNgoi != null ? ve.ChoNgoi.SoGhe : "",
                ve.ThanhToans,
                PhuongThucThanhToan = phuongThucThanhToan,
                ThoiGianThanhToan = thoiGianThanhToan,
                QRCode = qrCodeData
            };
            return View(detail);
        }

        // ADMIN: Hủy vé (và trả ghế)
        [HttpPost]
        public async Task<IActionResult> AdminCancelBooking(int id, string? lyDo)
        {
            var ve = await _context.Ves
                .Include(v => v.ChoNgoi)
                .Include(v => v.ChuyenXe)
                .FirstOrDefaultAsync(v => v.VeId == id);
            if (ve == null)
                return NotFound();
            if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                return Json(new { success = false, message = "Vé đã bị hủy trước đó." });

            // Chỉ cho phép hủy nếu chưa quá hạn (chưa khởi hành)
            if (ve.ChuyenXe != null && ve.ChuyenXe.NgayKhoiHanh <= DateTime.Now)
                return Json(new { success = false, message = "Chuyến xe đã khởi hành, không thể hủy vé." });

            ve.VeTrangThai = TrangThaiVe.DaHuy;
            ve.NgayHuy = DateTime.Now;
            ve.LyDoHuy = lyDo;
            await _context.SaveChangesAsync();

            // Trả ghế về trạng thái chưa đặt (nếu có)
            if (ve.ChoNgoiId.HasValue)
            {
                var choNgoi = ve.ChoNgoi;
                // Nếu có logic trạng thái ghế, cập nhật tại đây
                // choNgoi.TrangThai = ...
            }

            // Gửi email thông báo hủy nếu có email
            if (!string.IsNullOrEmpty(ve.Email))
            {
                try
                {
                    await SendTicketCancelAsync(ve.Email, ve.TenKhach, ve.MaVe, ve.ChuyenXe?.NgayKhoiHanh, ve.ChoNgoi?.SoGhe, lyDo);
                }
                catch { /* Ghi log nếu cần */ }
            }
            return Json(new { success = true, message = "Đã hủy vé thành công." });
        }

        // ADMIN: Xác nhận thanh toán thủ công
        [HttpPost]
        public async Task<IActionResult> AdminConfirmPayment(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ThanhToans)
                .FirstOrDefaultAsync(v => v.VeId == id);
            if (ve == null)
                return NotFound();
            if (ve.VeTrangThai == TrangThaiVe.DaThanhToan)
                return Json(new { success = false, message = "Vé đã được xác nhận thanh toán." });

            // Thêm bản ghi thanh toán thủ công
            var thanhToan = new ThanhToan
            {
                VeId = ve.VeId,
                SoTien = ve.GiaVe,
                NgayThanhToan = DateTime.Now,
                PhuongThucThanhToan = PhuongThucThanhToan.TaiQuay, // Sử dụng enum hợp lệ
                MaGiaoDich = $"ADMIN-{DateTime.Now:yyyyMMddHHmmss}-{ve.VeId}"
            };
            _context.ThanhToans.Add(thanhToan);
            ve.VeTrangThai = TrangThaiVe.DaThanhToan;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã xác nhận thanh toán thành công." });
        }

        // ADMIN: Xuất dữ liệu booking với filter
        public async Task<IActionResult> Export(DateTime? ngayDi, int? chuyenXeId, TrangThaiVe? trangThaiVe, string? tenKhach,
            DateTime? tuNgay, DateTime? denNgay, string? soDienThoai, string? email, string? maVe,
            decimal? giaVeMin, decimal? giaVeMax, bool? trangThaiThanhToan, string format = "csv")
        {
            try
            {
                var query = _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .AsQueryable();

                // Apply same filters as AdminBookingList
                if (ngayDi.HasValue)
                {
                    query = query.Where(v => v.ChuyenXe != null && v.ChuyenXe.NgayKhoiHanh.Date == ngayDi.Value.Date);
                }
                if (chuyenXeId.HasValue)
                {
                    query = query.Where(v => v.ChuyenXeId == chuyenXeId.Value);
                }
                if (trangThaiVe.HasValue)
                {
                    query = query.Where(v => v.VeTrangThai == trangThaiVe.Value);
                }
                if (!string.IsNullOrEmpty(tenKhach))
                {
                    query = query.Where(v => v.TenKhach.Contains(tenKhach));
                }
                if (tuNgay.HasValue)
                {
                    query = query.Where(v => v.NgayDat.Date >= tuNgay.Value.Date);
                }
                if (denNgay.HasValue)
                {
                    query = query.Where(v => v.NgayDat.Date <= denNgay.Value.Date);
                }
                if (!string.IsNullOrEmpty(soDienThoai))
                {
                    query = query.Where(v => v.SoDienThoai.Contains(soDienThoai));
                }
                if (!string.IsNullOrEmpty(email))
                {
                    query = query.Where(v => v.Email.Contains(email));
                }
                if (!string.IsNullOrEmpty(maVe))
                {
                    query = query.Where(v => v.MaVe.Contains(maVe));
                }
                if (giaVeMin.HasValue)
                {
                    query = query.Where(v => v.GiaVe >= giaVeMin.Value);
                }
                if (giaVeMax.HasValue)
                {
                    query = query.Where(v => v.GiaVe <= giaVeMax.Value);
                }
                if (trangThaiThanhToan.HasValue)
                {
                    if (trangThaiThanhToan.Value)
                    {
                        query = query.Where(v => v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong));
                    }
                    else
                    {
                        query = query.Where(v => !v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong));
                    }
                }

                var ves = await query.OrderByDescending(v => v.NgayDat).ToListAsync();

                if (!ves.Any())
                {
                    TempData["Warning"] = "Không có dữ liệu để xuất";
                    return RedirectToAction("AdminBookingList");
                }

                var fileName = GenerateExportFileName(tenKhach, chuyenXeId, trangThaiVe?.ToString(), tuNgay, denNgay, format);

                if (format.ToLower() == "excel")
                {
                    return await ExportToExcel(ves, fileName);
                }
                else
                {
                    return ExportToCsv(ves, fileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while exporting booking data");
                TempData["Error"] = "Có lỗi xảy ra khi xuất dữ liệu. Vui lòng thử lại.";
                return RedirectToAction("AdminBookingList");
            }
        }

        // ADMIN: Xuất danh sách hành khách ra Excel cho chuyến xe
        public async Task<IActionResult> ExportPassengerList(int chuyenXeId)
        {
            var chuyenXe = await _context.ChuyenXes
                .Include(cx => cx.TuyenDuong)
                .FirstOrDefaultAsync(cx => cx.ChuyenXeId == chuyenXeId);
            if (chuyenXe == null)
                return NotFound();
            var ves = await _context.Ves
                .Where(v => v.ChuyenXeId == chuyenXeId && v.VeTrangThai != TrangThaiVe.DaHuy)
                .Include(v => v.ChoNgoi)
                .ToListAsync();

            // Tạo file Excel bằng EPPlus
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("HanhKhach");
                ws.Cells[1, 1].Value = "Họ tên khách";
                ws.Cells[1, 2].Value = "Ghế đã đặt";
                ws.Cells[1, 3].Value = "SĐT";
                ws.Cells[1, 4].Value = "Ghi chú";
                int row = 2;
                foreach (var v in ves)
                {
                    ws.Cells[row, 1].Value = v.TenKhach;
                    ws.Cells[row, 2].Value = v.ChoNgoi?.SoGhe;
                    ws.Cells[row, 3].Value = v.SoDienThoai;
                    ws.Cells[row, 4].Value = v.GhiChu;
                    row++;
                }
                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                var fileName = $"DS_HanhKhach_{chuyenXeId}_{DateTime.Now:yyyyMMddHHmmss}.xlsx";
                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
        }

        // ADMIN: Xem/in vé PDF
        public async Task<IActionResult> PrintTicket(int id)
        {
            var ve = await _context.Ves
                .Include(v => v.ChuyenXe)
                    .ThenInclude(cx => cx.TuyenDuong)
                .Include(v => v.ChuyenXe)
                    .ThenInclude(cx => cx.Xe)
                .Include(v => v.ChoNgoi)
                .Include(v => v.ThanhToans)
                .FirstOrDefaultAsync(v => v.VeId == id);
            if (ve == null)
                return NotFound();

            // Tạo dữ liệu QR code với thông tin chi tiết
            var qrData = $"TICKET:{ve.MaVe}|CUSTOMER:{ve.TenKhach}|PHONE:{ve.SoDienThoai}|SEAT:{ve.ChoNgoi?.SoGhe}|DATE:{ve.ChuyenXe?.NgayKhoiHanh:yyyy-MM-dd HH:mm}";
            var qrCodeUrl = $"https://api.qrserver.com/v1/create-qr-code/?size=200x200&data={Uri.EscapeDataString(qrData)}&format=png&margin=10";

            // Tạo HTML vé với thiết kế đẹp
            var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                           (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");
            var trangThaiThanhToan = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true
                                   ? "Đã thanh toán" : "Chưa thanh toán";

            var html = $@"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>Vé xe - {ve.MaVe}</title>
    <style>
        body {{
            font-family: 'Arial', sans-serif;
            margin: 0;
            padding: 20px;
            background-color: #f5f5f5;
        }}
        .ticket {{
            max-width: 800px;
            margin: 0 auto;
            background: white;
            border-radius: 15px;
            box-shadow: 0 10px 30px rgba(0,0,0,0.1);
            overflow: hidden;
        }}
        .ticket-header {{
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }}
        .ticket-header h1 {{
            margin: 0;
            font-size: 2.5em;
            font-weight: bold;
        }}
        .ticket-header .subtitle {{
            margin: 10px 0 0 0;
            font-size: 1.2em;
            opacity: 0.9;
        }}
        .ticket-body {{
            padding: 40px;
        }}
        .ticket-info {{
            display: grid;
            grid-template-columns: 1fr 1fr;
            gap: 30px;
            margin-bottom: 30px;
        }}
        .info-section {{
            background: #f8f9fa;
            padding: 20px;
            border-radius: 10px;
            border-left: 4px solid #667eea;
        }}
        .info-section h3 {{
            margin: 0 0 15px 0;
            color: #333;
            font-size: 1.1em;
            font-weight: bold;
        }}
        .info-row {{
            display: flex;
            justify-content: space-between;
            margin-bottom: 8px;
            padding: 5px 0;
            border-bottom: 1px dotted #ddd;
        }}
        .info-row:last-child {{
            border-bottom: none;
        }}
        .info-label {{
            font-weight: bold;
            color: #555;
        }}
        .info-value {{
            color: #333;
        }}
        .qr-section {{
            text-align: center;
            background: #f8f9fa;
            padding: 30px;
            border-radius: 10px;
            margin: 30px 0;
        }}
        .qr-section h3 {{
            margin: 0 0 20px 0;
            color: #333;
        }}
        .qr-code {{
            border: 3px solid #667eea;
            border-radius: 10px;
            padding: 10px;
            background: white;
            display: inline-block;
        }}
        .ticket-footer {{
            background: #f8f9fa;
            padding: 20px 40px;
            text-align: center;
            border-top: 2px dashed #ddd;
        }}
        .footer-note {{
            color: #666;
            font-size: 0.9em;
            margin: 5px 0;
        }}
        .status-badge {{
            display: inline-block;
            padding: 8px 16px;
            border-radius: 20px;
            font-weight: bold;
            font-size: 0.9em;
        }}
        .status-paid {{
            background: #d4edda;
            color: #155724;
            border: 1px solid #c3e6cb;
        }}
        .status-unpaid {{
            background: #f8d7da;
            color: #721c24;
            border: 1px solid #f5c6cb;
        }}
        @media print {{
            body {{ background: white; }}
            .ticket {{ box-shadow: none; }}
        }}
    </style>
</head>
<body>
    <div class='ticket'>
        <div class='ticket-header'>
            <h1>VÉ XE KHÁCH</h1>
            <div class='subtitle'>Mã vé: {ve.MaVe}</div>
        </div>

        <div class='ticket-body'>
            <div class='ticket-info'>
                <div class='info-section'>
                    <h3>Thông tin khách hàng</h3>
                    <div class='info-row'>
                        <span class='info-label'>Họ tên:</span>
                        <span class='info-value'>{ve.TenKhach}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Số điện thoại:</span>
                        <span class='info-value'>{ve.SoDienThoai}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Email:</span>
                        <span class='info-value'>{ve.Email ?? "Không có"}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Số ghế:</span>
                        <span class='info-value'>{ve.ChoNgoi?.SoGhe ?? "Chưa chọn"}</span>
                    </div>
                </div>

                <div class='info-section'>
                    <h3>Thông tin chuyến xe</h3>
                    <div class='info-row'>
                        <span class='info-label'>Tuyến đường:</span>
                        <span class='info-value'>{tuyenDuong}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Ngày khởi hành:</span>
                        <span class='info-value'>{ve.ChuyenXe?.NgayKhoiHanh:dd/MM/yyyy}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Giờ khởi hành:</span>
                        <span class='info-value'>{ve.ChuyenXe?.NgayKhoiHanh:HH:mm}</span>
                    </div>
                    <div class='info-row'>
                        <span class='info-label'>Biển số xe:</span>
                        <span class='info-value'>{ve.ChuyenXe?.Xe?.BienSoXe ?? "Chưa xác định"}</span>
                    </div>
                </div>
            </div>

            <div class='info-section'>
                <h3>Thông tin thanh toán</h3>
                <div class='info-row'>
                    <span class='info-label'>Giá vé:</span>
                    <span class='info-value'>{ve.GiaVe:N0} VNĐ</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Ngày đặt:</span>
                    <span class='info-value'>{ve.NgayDat:dd/MM/yyyy HH:mm}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Trạng thái thanh toán:</span>
                    <span class='info-value'>
                        <span class='status-badge {(trangThaiThanhToan == "Đã thanh toán" ? "status-paid" : "status-unpaid")}'>
                            {trangThaiThanhToan}
                        </span>
                    </span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Trạng thái vé:</span>
                    <span class='info-value'>{StatusHelper.GetStatusText(ve.VeTrangThai)}</span>
                </div>
            </div>

            <div class='qr-section'>
                <h3>Mã QR để kiểm tra vé</h3>
                <div class='qr-code'>
                    <img src='{qrCodeUrl}' alt='QR Code' />
                </div>
                <p style='margin: 15px 0 0 0; color: #666; font-size: 0.9em;'>
                    Vui lòng xuất trình mã QR này khi lên xe
                </p>
            </div>
        </div>

        <div class='ticket-footer'>
            <div class='footer-note'>
                <strong>Lưu ý quan trọng:</strong>
            </div>
            <div class='footer-note'>
                • Vui lòng có mặt tại bến xe trước giờ khởi hành 15 phút
            </div>
            <div class='footer-note'>
                • Mang theo giấy tờ tùy thân khi đi xe
            </div>
            <div class='footer-note'>
                • Liên hệ hotline để được hỗ trợ: 1900-xxxx
            </div>
            <div class='footer-note' style='margin-top: 15px; font-size: 0.8em;'>
                Vé được in lúc: {DateTime.Now:dd/MM/yyyy HH:mm}
            </div>
        </div>
    </div>
</body>
</html>";

            return Content(html, "text/html");
        }

        // ADMIN: Trang quét QR code
        public IActionResult QRScanner()
        {
            return View();
        }

        // API: Kiểm tra QR code vé
        [HttpGet]
        public async Task<IActionResult> VerifyQRCode(string qrData)
        {
            try
            {
                if (string.IsNullOrEmpty(qrData))
                {
                    return Json(new { success = false, message = "Mã QR không hợp lệ" });
                }

                // Sử dụng QRCodeService để parse QR data
                var ticketData = _qrCodeService.ParseTicketQRData(qrData);
                string maVe = "";

                if (ticketData != null)
                {
                    // QR data có cấu trúc (JSON hoặc text format)
                    maVe = ticketData.TicketCode;
                }
                else
                {
                    // Fallback: thử parse format cũ hoặc coi như mã vé trực tiếp
                    if (qrData.Contains("TICKET:"))
                    {
                        var parts = qrData.Split('|');
                        if (parts.Length >= 1 && parts[0].StartsWith("TICKET:"))
                        {
                            maVe = parts[0].Replace("TICKET:", "");
                        }
                    }
                    else
                    {
                        // Coi như mã vé trực tiếp
                        maVe = qrData.Trim();
                    }
                }

                if (string.IsNullOrEmpty(maVe))
                {
                    return Json(new { success = false, message = "Không thể đọc mã vé từ QR code" });
                }

                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.TuyenDuong)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.Xe)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .FirstOrDefaultAsync(v => v.MaVe == maVe);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé với mã này" });
                }

                // Validate ticket status
                if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                {
                    return Json(new {
                        success = false,
                        message = "Vé đã bị hủy",
                        ticketInfo = new {
                            maVe = ve.MaVe,
                            trangThai = "Đã hủy",
                            lyDoHuy = ve.LyDoHuy,
                            ngayHuy = ve.NgayHuy?.ToString("dd/MM/yyyy HH:mm")
                        }
                    });
                }

                // Lấy thông tin tuyến đường
                var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                               (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");

                // Lấy thông tin khuyến mãi nếu có
                var lichSuKhuyenMai = await _context.LichSuKhuyenMais
                    .Include(l => l.KhuyenMai)
                    .FirstOrDefaultAsync(l => l.VeId == ve.VeId);

                // Tạo thông tin vé đầy đủ
                var fullTicketInfo = new {
                    maVe = ve.MaVe,
                    tenKhach = ve.TenKhach,
                    soDienThoai = ve.SoDienThoai,
                    email = ve.Email,
                    tuyenDuong = tuyenDuong,
                    ngayKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm"),
                    soGhe = ve.ChoNgoi?.SoGhe,
                    bienSoXe = ve.ChuyenXe?.Xe?.BienSoXe,
                    giaVe = ve.GiaVe.ToString("N0"),
                    trangThai = StatusHelper.GetStatusText(ve.VeTrangThai),
                    ngayDat = ve.NgayDat.ToString("dd/MM/yyyy HH:mm"),
                    // Thông tin khuyến mãi
                    maKhuyenMai = lichSuKhuyenMai?.MaKhuyenMai,
                    giamGia = lichSuKhuyenMai?.GiaTriGiam ?? 0,
                    tenKhuyenMai = lichSuKhuyenMai?.KhuyenMai?.TenKhuyenMai
                };

                if (ve.VeTrangThai == TrangThaiVe.DaSuDung)
                {
                    return Json(new {
                        success = false,
                        message = "Vé đã được sử dụng",
                        ticketInfo = fullTicketInfo
                    });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                {
                    return Json(new {
                        success = false,
                        message = "Vé đã bị hủy",
                        ticketInfo = fullTicketInfo
                    });
                }

                // Check if payment is completed
                var isPaid = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true;
                if (!isPaid && ve.VeTrangThai == TrangThaiVe.DaDat)
                {
                    return Json(new {
                        success = false,
                        message = "Vé chưa được thanh toán",
                        ticketInfo = fullTicketInfo
                    });
                }

                // Return valid ticket info
                return Json(new {
                    success = true,
                    message = "Vé hợp lệ",
                    ticketInfo = fullTicketInfo
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying QR code: {QRData}", qrData);
                return Json(new { success = false, message = "Có lỗi xảy ra khi kiểm tra mã QR" });
            }
        }

        // API: Đánh dấu vé đã sử dụng thông qua QR code
        [HttpPost]
        public async Task<IActionResult> MarkTicketUsed([FromBody] MarkTicketUsedRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.QrData))
                {
                    return Json(new { success = false, message = "Mã QR không hợp lệ" });
                }

                // Sử dụng QRCodeService để parse QR data
                var ticketData = _qrCodeService.ParseTicketQRData(request.QrData);
                string maVe = "";

                if (ticketData != null)
                {
                    maVe = ticketData.TicketCode;
                }
                else
                {
                    // Fallback: thử parse format cũ hoặc coi như mã vé trực tiếp
                    if (request.QrData.Contains("TICKET:"))
                    {
                        var parts = request.QrData.Split('|');
                        if (parts.Length >= 1 && parts[0].StartsWith("TICKET:"))
                        {
                            maVe = parts[0].Replace("TICKET:", "");
                        }
                    }
                    else
                    {
                        maVe = request.QrData.Trim();
                    }
                }

                if (string.IsNullOrEmpty(maVe))
                {
                    return Json(new { success = false, message = "Không thể đọc mã vé từ QR code" });
                }

                var ve = await _context.Ves.FirstOrDefaultAsync(v => v.MaVe == maVe);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé với mã này" });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaHuy)
                {
                    return Json(new { success = false, message = "Vé đã bị hủy, không thể sử dụng" });
                }

                if (ve.VeTrangThai == TrangThaiVe.DaSuDung)
                {
                    return Json(new { success = false, message = "Vé đã được sử dụng trước đó" });
                }

                // Mark as used
                ve.VeTrangThai = TrangThaiVe.DaSuDung;
                if (!string.IsNullOrEmpty(request.GhiChu))
                {
                    ve.GhiChu = request.GhiChu;
                }

                await _context.SaveChangesAsync();

                return Json(new {
                    success = true,
                    message = "Đã đánh dấu vé đã sử dụng thành công",
                    ticketInfo = new {
                        maVe = ve.MaVe,
                        tenKhach = ve.TenKhach
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking ticket as used: {QRData}", request.QrData);
                return Json(new { success = false, message = "Có lỗi xảy ra khi cập nhật trạng thái vé" });
            }
        }

        // ADMIN/Nhà xe: Cập nhật trạng thái chuyến đi cho từng vé
        [HttpPost]
        public async Task<IActionResult> UpdateTripStatus(int veId, string trangThai)
        {
            var ve = await _context.Ves.Include(v => v.ChuyenXe).FirstOrDefaultAsync(v => v.VeId == veId);
            if (ve == null)
                return NotFound();
            if (ve.ChuyenXe == null)
                return Json(new { success = false, message = "Không tìm thấy chuyến xe." });

            // Các trạng thái: DaDon, KhongCoMat, HuyChuyen
            switch (trangThai)
            {
                case "DaDon":
                    ve.VeTrangThai = TrangThaiVe.DaSuDung;
                    break;
                case "KhongCoMat":
                    ve.VeTrangThai = TrangThaiVe.DaHuy;
                    ve.LyDoHuy = "Khách không có mặt khi xe xuất bến";
                    ve.NgayHuy = DateTime.Now;
                    break;
                case "HuyChuyen":
                    if (ve.ChuyenXe != null)
                    {
                        ve.ChuyenXe.TrangThaiChuyenXe = TrangThaiChuyenXe.DaHuy;
                    }
                    break;
                default:
                    return Json(new { success = false, message = "Trạng thái không hợp lệ." });
            }
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã cập nhật trạng thái chuyến đi." });
        }

        // ADMIN: Thống kê đặt vé
        public async Task<IActionResult> BookingStatistics(DateTime? from, DateTime? to)
        {
            var start = from ?? DateTime.Today.AddDays(-30);
            var end = to ?? DateTime.Today;
            var ves = await _context.Ves
                .Include(v => v.ThanhToans)
                .Where(v => v.NgayDat >= start && v.NgayDat <= end)
                .ToListAsync();

            // Basic statistics
            var totalTickets = ves.Count;
            var totalRevenue = ves.Where(v => v.ThanhToans.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong))
                                 .Sum(v => v.GiaVe);
            var cancelRate = ves.Count == 0 ? 0 : ves.Count(v => v.VeTrangThai == TrangThaiVe.DaHuy) * 100.0 / ves.Count;
            var daysDiff = (end - start).Days + 1;
            var avgPerDay = daysDiff > 0 ? (double)totalTickets / daysDiff : 0;

            // Status counts
            var statusCounts = new
            {
                DaDat = ves.Count(v => v.VeTrangThai == TrangThaiVe.DaDat),
                DaThanhToan = ves.Count(v => v.VeTrangThai == TrangThaiVe.DaThanhToan),
                DaSuDung = ves.Count(v => v.VeTrangThai == TrangThaiVe.DaSuDung),
                DaHuy = ves.Count(v => v.VeTrangThai == TrangThaiVe.DaHuy),
                DaHoanThanh = ves.Count(v => v.VeTrangThai == TrangThaiVe.DaHoanThanh)
            };

            // Time-based groupings
            var byDay = ves.GroupBy(v => v.NgayDat.Date)
                .Select(g => new { Date = g.Key, Total = g.Count() }).OrderBy(x => x.Date).ToList();
            var byWeek = ves.GroupBy(v => System.Globalization.CultureInfo.CurrentCulture.Calendar.GetWeekOfYear(v.NgayDat, System.Globalization.CalendarWeekRule.FirstDay, DayOfWeek.Monday))
                .Select(g => new { Week = g.Key, Total = g.Count() }).OrderBy(x => x.Week).ToList();
            var byMonth = ves.GroupBy(v => new { v.NgayDat.Year, v.NgayDat.Month })
                .Select(g => new { g.Key.Year, g.Key.Month, Total = g.Count() }).OrderBy(x => x.Year).ThenBy(x => x.Month).ToList();

            // Top lists
            var topTrips = ves.GroupBy(v => v.ChuyenXeId).Select(g => new { ChuyenXeId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(5).ToList();
            var topSeats = ves.GroupBy(v => v.ChoNgoiId).Select(g => new { ChoNgoiId = g.Key, Count = g.Count() }).OrderByDescending(x => x.Count).Take(5).ToList();

            // Set ViewBag data
            ViewBag.TotalTickets = totalTickets;
            ViewBag.TotalRevenue = totalRevenue;
            ViewBag.CancelRate = cancelRate;
            ViewBag.AvgPerDay = avgPerDay;
            ViewBag.StatusCounts = statusCounts;
            ViewBag.TopTrips = topTrips;
            ViewBag.TopSeats = topSeats;
            ViewBag.ByDay = byDay;
            ViewBag.ByWeek = byWeek;
            ViewBag.ByMonth = byMonth;
            ViewBag.From = start;
            ViewBag.To = end;

            return View();
        }

        // PHÂN QUYỀN: Lọc danh sách vé theo vai trò
        // Gọi hàm này trong các action danh sách/chi tiết nếu cần
        private IQueryable<Ve> FilterByRole(IQueryable<Ve> query)
        {
            if (User.IsInRole("Admin"))
            {
                return query;
            }
            else if (User.IsInRole("NhaXe"))
            {
                // So sánh theo tên nhà xe (string)
                var nhaXeName = GetCurrentNhaXeName(); // TODO: Lấy tên nhà xe từ user đăng nhập
                return query.Where(v => v.ChuyenXe != null && v.ChuyenXe.Xe != null && v.ChuyenXe.Xe.NhaXe == nhaXeName);
            }
            else
            {
                var userId = GetCurrentUserId();
                return query.Where(v => v.NguoiDungId == userId);
            }
        }
        // Stub: Lấy tên nhà xe hiện tại (bạn cần thay bằng logic thực tế)
        private string GetCurrentNhaXeName() => "Nhà xe A"; // TODO: Lấy từ user đăng nhập
        // Stub: Lấy ID nhà xe hiện tại (bạn cần thay bằng logic thực tế)
        private int GetCurrentNhaXeId() => 1; // TODO: Lấy từ user đăng nhập
        // Stub: Lấy ID user hiện tại (bạn cần thay bằng logic thực tế)
        private int GetCurrentUserId() => 1; // TODO: Lấy từ user đăng nhập

        // Hỗ trợ gọi đúng hàm gửi email hủy vé
        private async Task<bool> SendTicketCancelAsync(string toEmail, string customerName, string ticketCode, DateTime? departureTime, string? seatInfo, string? reason)
        {
            string tripInfo = departureTime.HasValue ? $"Khởi hành: {departureTime.Value:dd/MM/yyyy HH:mm}" : "";
            return await _emailService.SendTicketCancellationAsync(toEmail, customerName, ticketCode, tripInfo, reason);
        }

        // POST: Booking/CheckPromo - Kiểm tra mã khuyến mãi
        [HttpPost]
        public async Task<IActionResult> CheckPromo([FromBody] CheckPromoRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.MaKhuyenMai))
                return Json(new { success = false, message = "Vui lòng nhập mã khuyến mãi" });

            var now = DateTime.Now;
            var promo = await _context.KhuyenMais.FirstOrDefaultAsync(k => k.MaKhuyenMai == request.MaKhuyenMai && k.TrangThaiHoatDong);
            if (promo == null)
                return Json(new { success = false, message = "Mã khuyến mãi không tồn tại hoặc đã ngừng hoạt động" });

            if (promo.NgayBatDau > now || promo.NgayKetThuc < now)
                return Json(new { success = false, message = "Mã khuyến mãi đã hết hạn hoặc chưa bắt đầu" });

            if (promo.SoLuongToiDa.HasValue && promo.SoLuongToiDa.Value <= 0)
                return Json(new { success = false, message = "Mã khuyến mãi đã hết lượt sử dụng" });

            // Điều kiện giá trị đơn hàng tối thiểu
            if (promo.GiaTriDonHangToiThieu.HasValue && request.TongTien < promo.GiaTriDonHangToiThieu.Value)
                return Json(new { success = false, message = $"Đơn hàng phải từ {promo.GiaTriDonHangToiThieu.Value:N0} VNĐ mới áp dụng mã này" });

            // Tính giảm giá
            decimal giamGia = 0;
            if (promo.LoaiKhuyenMai == LoaiKhuyenMai.GiamSoTien)
            {
                giamGia = promo.GiaTri;
            }
            else if (promo.LoaiKhuyenMai == LoaiKhuyenMai.GiamPhanTram)
            {
                giamGia = request.TongTien * promo.GiaTri / 100;
                if (promo.GiaTriToiDa.HasValue && giamGia > promo.GiaTriToiDa.Value)
                    giamGia = promo.GiaTriToiDa.Value;
            }
            if (giamGia > request.TongTien) giamGia = request.TongTien;

            return Json(new { success = true, giamGia, message = $"Áp dụng thành công, giảm {giamGia:N0} VNĐ" });
        }

        // ADMIN: Xử lý hoàn tiền
        [HttpPost]
        public async Task<IActionResult> ProcessRefund(int veId, string method, string reason, string bankInfo = "")
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Include(v => v.ThanhToans)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                if (ve.VeTrangThai != TrangThaiVe.DaHuy)
                {
                    return Json(new { success = false, message = "Chỉ có thể hoàn tiền cho vé đã hủy" });
                }

                // Tính toán số tiền hoàn lại
                var originalPrice = ve.GiaVe;
                var cancellationFee = CalculateCancellationFee(ve);
                var refundAmount = originalPrice - cancellationFee;

                if (refundAmount <= 0)
                {
                    return Json(new { success = false, message = "Số tiền hoàn lại phải lớn hơn 0" });
                }

                // Tạo giao dịch hoàn tiền
                var refundTransaction = new ThanhToan
                {
                    VeId = veId,
                    MaGiaoDich = GenerateTransactionCode(),
                    SoTien = refundAmount,
                    PhuongThucThanhToan = PhuongThucThanhToan.HoanTien,
                    TrangThai = TrangThaiThanhToan.ThanhCong,
                    NgayThanhToan = DateTime.Now,
                    GhiChu = $"Hoàn tiền - {reason}",
                    MaPhanHoi = !string.IsNullOrEmpty(bankInfo) ? bankInfo : method
                };

                _context.ThanhToans.Add(refundTransaction);

                // Cập nhật trạng thái vé
                ve.VeTrangThai = TrangThaiVe.DaHoanTien;
                ve.GhiChu = $"{ve.GhiChu ?? ""}\nHoàn tiền: {refundAmount:N0} VNĐ - {reason}".Trim();

                await _context.SaveChangesAsync();

                // Gửi thông báo hoàn tiền
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendRefundNotification(ve, refundAmount, method);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending refund notification for ticket {TicketCode}", ve.MaVe);
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"Hoàn tiền thành công {refundAmount:N0} VNĐ",
                    refundAmount = refundAmount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing refund for ticket {VeId}", veId);
                return Json(new { success = false, message = "Có lỗi xảy ra khi xử lý hoàn tiền" });
            }
        }

        // Gửi thông báo hoàn tiền
        private async Task SendRefundNotification(Ve ve, decimal refundAmount, string method)
        {
            try
            {
                if (string.IsNullOrEmpty(ve.Email))
                    return;

                var subject = $"Thông báo hoàn tiền vé {ve.MaVe}";
                var message = $@"
Kính gửi {ve.TenKhach},

Chúng tôi xin thông báo đã xử lý hoàn tiền cho vé {ve.MaVe}.

Thông tin hoàn tiền:
- Số tiền hoàn lại: {refundAmount:N0} VNĐ
- Phương thức: {GetRefundMethodText(method)}
- Thời gian xử lý: {DateTime.Now:dd/MM/yyyy HH:mm}

Số tiền sẽ được chuyển về tài khoản của quý khách trong vòng 3-5 ngày làm việc.

Cảm ơn quý khách đã sử dụng dịch vụ của chúng tôi.

Trân trọng,
Đội ngũ hỗ trợ khách hàng";

                await _emailService.SendEmailAsync(ve.Email, subject, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending refund notification email");
            }
        }

        private string GetRefundMethodText(string method)
        {
            return method switch
            {
                "bank_transfer" => "Chuyển khoản ngân hàng",
                "cash" => "Tiền mặt tại quầy",
                "original_method" => "Hoàn về phương thức gốc",
                _ => method
            };
        }

        // ADMIN: Lấy danh sách chuyến xe khả dụng để chuyển vé
        [HttpGet]
        public async Task<IActionResult> GetAvailableTrips(int excludeVeId)
        {
            try
            {
                var currentVe = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .FirstOrDefaultAsync(v => v.VeId == excludeVeId);

                if (currentVe?.ChuyenXe == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé hiện tại" });
                }

                // Lấy các chuyến xe cùng tuyến và sau thời điểm hiện tại
                var availableTrips = await _context.ChuyenXes
                    .Include(c => c.TuyenDuong)
                    .Include(c => c.Xe)
                    .Where(c => c.ChuyenXeId != currentVe.ChuyenXeId &&
                               c.NgayKhoiHanh > DateTime.Now &&
                               c.TrangThaiChuyenXe == TrangThaiChuyenXe.HoatDong &&
                               (c.DiemDi == currentVe.ChuyenXe.DiemDi && c.DiemDen == currentVe.ChuyenXe.DiemDen))
                    .OrderBy(c => c.NgayKhoiHanh)
                    .Take(20)
                    .ToListAsync();

                var trips = availableTrips.Select(c => new
                {
                    id = c.ChuyenXeId,
                    route = c.TuyenDuong?.TenTuyen ?? $"{c.DiemDi} - {c.DiemDen}",
                    departureTime = c.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm"),
                    price = c.Gia,
                    availableSeats = c.Xe?.SoGhe - (c.Ves?.Count(v => v.VeTrangThai != TrangThaiVe.DaHuy) ?? 0)
                }).ToList();

                return Json(new { success = true, trips });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting available trips");
                return Json(new { success = false, message = "Có lỗi xảy ra khi tải danh sách chuyến xe" });
            }
        }

        // ADMIN: Check-in hành khách
        [HttpPost]
        public async Task<IActionResult> CheckInPassenger(int veId, DateTime checkinTime, string location,
            bool identityVerified, string healthStatus, string healthNote = "", string note = "")
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                if (ve.VeTrangThai != TrangThaiVe.DaThanhToan)
                {
                    return Json(new { success = false, message = "Chỉ có thể check-in cho vé đã thanh toán" });
                }

                // Cập nhật trạng thái vé
                ve.VeTrangThai = TrangThaiVe.DaSuDung;

                // Tạo bản ghi check-in
                var checkinInfo = new
                {
                    CheckinTime = checkinTime,
                    Location = GetLocationText(location),
                    IdentityVerified = identityVerified,
                    HealthStatus = healthStatus,
                    HealthNote = healthNote,
                    Note = note,
                    ProcessedBy = "Admin", // Có thể lấy từ session user
                    ProcessedAt = DateTime.Now
                };

                // Cập nhật ghi chú vé với thông tin check-in
                var checkinNote = $"Check-in: {checkinTime:dd/MM/yyyy HH:mm} tại {GetLocationText(location)}";
                if (healthStatus == "concern" && !string.IsNullOrEmpty(healthNote))
                {
                    checkinNote += $" - Sức khỏe: {healthNote}";
                }
                if (!string.IsNullOrEmpty(note))
                {
                    checkinNote += $" - Ghi chú: {note}";
                }

                ve.GhiChu = $"{ve.GhiChu ?? ""}\n{checkinNote}".Trim();

                await _context.SaveChangesAsync();

                // Gửi thông báo check-in
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendCheckinNotification(ve, checkinTime, GetLocationText(location));
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending check-in notification for ticket {TicketCode}", ve.MaVe);
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"Check-in thành công cho vé {ve.MaVe}",
                    checkinTime = checkinTime.ToString("dd/MM/yyyy HH:mm")
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking in passenger for ticket {VeId}", veId);
                return Json(new { success = false, message = "Có lỗi xảy ra khi check-in hành khách" });
            }
        }

        private string GetLocationText(string location)
        {
            return location switch
            {
                "ben_xe" => "Bến xe",
                "tren_xe" => "Trên xe",
                "diem_don" => "Điểm đón",
                _ => location
            };
        }

        // Gửi thông báo check-in
        private async Task SendCheckinNotification(Ve ve, DateTime checkinTime, string location)
        {
            try
            {
                if (string.IsNullOrEmpty(ve.Email))
                    return;

                var subject = $"Xác nhận check-in vé {ve.MaVe}";
                var message = $@"
Kính gửi {ve.TenKhach},

Chúng tôi xác nhận quý khách đã check-in thành công.

Thông tin check-in:
- Mã vé: {ve.MaVe}
- Thời gian check-in: {checkinTime:dd/MM/yyyy HH:mm}
- Vị trí: {location}

Chúc quý khách có chuyến đi an toàn và thuận lợi!

Trân trọng,
Đội ngũ hỗ trợ khách hàng";

                await _emailService.SendEmailAsync(ve.Email, subject, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending check-in notification email");
            }
        }

        // ADMIN: Cập nhật trạng thái vé
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateTicketStatus(int veId, string trangThai, string ghiChu = "")
        {
            try
            {
                if (veId <= 0)
                {
                    return Json(new { success = false, message = "ID vé không hợp lệ" });
                }

                if (string.IsNullOrWhiteSpace(trangThai))
                {
                    return Json(new { success = false, message = "Trạng thái không được để trống" });
                }

                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                // Parse enum từ string
                if (!Enum.TryParse<TrangThaiVe>(trangThai, out var newStatus))
                {
                    return Json(new { success = false, message = "Trạng thái không hợp lệ" });
                }

                // Validate business rules
                var validationResult = StatusHelper.ValidateStatusChange(ve.VeTrangThai, newStatus);
                if (!validationResult.IsValid)
                {
                    return Json(new { success = false, message = validationResult.ErrorMessage });
                }

                // Lưu trạng thái cũ để log
                var oldStatus = ve.VeTrangThai;

                // Cập nhật trạng thái
                ve.VeTrangThai = newStatus;

                // Cập nhật ghi chú nếu có
                if (!string.IsNullOrWhiteSpace(ghiChu))
                {
                    var currentNote = ve.GhiChu ?? "";
                    ve.GhiChu = $"{currentNote}\n[{DateTime.Now:dd/MM/yyyy HH:mm}] {ghiChu}".Trim();
                }

                // Cập nhật các trường liên quan theo trạng thái
                switch (newStatus)
                {
                    case TrangThaiVe.DaHuy:
                        ve.NgayHuy = DateTime.Now;
                        ve.LyDoHuy = ghiChu ?? "Hủy bởi admin";
                        break;
                    case TrangThaiVe.DaSuDung:
                        // Đánh dấu đã sử dụng (hành khách đã lên xe)
                        break;
                    case TrangThaiVe.DaHoanThanh:
                        // Đánh dấu hoàn thành chuyến đi
                        break;
                    case TrangThaiVe.DaHoanTien:
                        // Đánh dấu đã hoàn tiền
                        break;
                }

                await _context.SaveChangesAsync();

                // Log thay đổi
                _logger.LogInformation($"Ticket {ve.MaVe} status changed from {oldStatus} to {newStatus} by admin");

                // Gửi thông báo cho khách hàng (nếu cần)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await SendStatusChangeNotification(ve, oldStatus, newStatus);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error sending status change notification for ticket {TicketCode}", ve.MaVe);
                    }
                });

                return Json(new
                {
                    success = true,
                    message = $"Cập nhật trạng thái vé thành công từ '{StatusHelper.GetStatusText(oldStatus)}' sang '{StatusHelper.GetStatusText(newStatus)}'",
                    newStatus = StatusHelper.GetStatusText(newStatus),
                    newStatusValue = newStatus.ToString()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error updating ticket status for VeId: {veId}");
                return Json(new { success = false, message = "Có lỗi xảy ra khi cập nhật trạng thái vé. Vui lòng thử lại." });
            }
        }



        // Gửi thông báo thay đổi trạng thái
        private async Task SendStatusChangeNotification(Ve ve, TrangThaiVe oldStatus, TrangThaiVe newStatus)
        {
            try
            {
                if (string.IsNullOrEmpty(ve.Email))
                    return;

                var subject = $"Thông báo thay đổi trạng thái vé {ve.MaVe}";
                var message = $@"
Kính gửi {ve.TenKhach},

Trạng thái vé của quý khách đã được cập nhật:

- Mã vé: {ve.MaVe}
- Trạng thái cũ: {StatusHelper.GetStatusText(oldStatus)}
- Trạng thái mới: {StatusHelper.GetStatusText(newStatus)}
- Thời gian cập nhật: {DateTime.Now:dd/MM/yyyy HH:mm}

{GetStatusChangeMessage(newStatus)}

Cảm ơn quý khách đã sử dụng dịch vụ của chúng tôi.

Trân trọng,
Đội ngũ hỗ trợ khách hàng";

                await _emailService.SendEmailAsync(ve.Email, subject, message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending status change notification email");
            }
        }

        private string GetStatusChangeMessage(TrangThaiVe newStatus)
        {
            return newStatus switch
            {
                TrangThaiVe.DaThanhToan => "Vé của quý khách đã được thanh toán thành công.",
                TrangThaiVe.DaSuDung => "Quý khách đã check-in thành công. Chúc quý khách có chuyến đi an toàn!",
                TrangThaiVe.DaHoanThanh => "Chuyến đi của quý khách đã hoàn thành. Cảm ơn quý khách đã sử dụng dịch vụ!",
                TrangThaiVe.DaHuy => "Vé của quý khách đã bị hủy. Nếu có thắc mắc, vui lòng liên hệ với chúng tôi.",
                TrangThaiVe.DaHoanTien => "Chúng tôi đã xử lý hoàn tiền cho vé của quý khách.",
                _ => ""
            };
        }

        // ADMIN: Lấy thông tin vé cho modal
        [HttpGet]
        public async Task<IActionResult> GetTicketInfo(int veId)
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .ThenInclude(c => c.TuyenDuong)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                return Json(new
                {
                    success = true,
                    maVe = ve.MaVe,
                    tenKhach = ve.TenKhach,
                    soDienThoai = ve.SoDienThoai,
                    email = ve.Email,
                    tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ?? $"{ve.ChuyenXe?.DiemDi} - {ve.ChuyenXe?.DiemDen}",
                    ngayKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm"),
                    giaVe = ve.GiaVe,
                    trangThai = ve.VeTrangThai.ToString(),
                    trangThaiText = StatusHelper.GetStatusText(ve.VeTrangThai)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ticket info for VeId: {VeId}", veId);
                return Json(new { success = false, message = "Có lỗi xảy ra khi tải thông tin vé" });
            }
        }

        public class CheckPromoRequest
        {
            public string MaKhuyenMai { get; set; } = string.Empty;
            public decimal TongTien { get; set; }
        }

        // ADMIN: Thêm ghi chú cho vé
        [HttpPost]
        public async Task<IActionResult> AddNoteToTicket(int veId, string ghiChu)
        {
            var ve = await _context.Ves.FindAsync(veId);
            if (ve == null)
                return Json(new { success = false, message = "Không tìm thấy vé." });

            ve.GhiChu = ghiChu;
            await _context.SaveChangesAsync();
            return Json(new { success = true, message = "Đã cập nhật ghi chú." });
        }

        // ADMIN: Tính toán hoàn tiền
        [HttpGet]
        public async Task<IActionResult> CalculateRefund(int veId)
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                    .Include(v => v.ThanhToans)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    return Json(new { success = false, message = "Không tìm thấy vé" });
                }

                if (ve.VeTrangThai != TrangThaiVe.DaHuy)
                {
                    return Json(new { success = false, message = "Chỉ có thể hoàn tiền cho vé đã hủy" });
                }

                // Tính toán phí hủy dựa trên thời gian
                var originalPrice = ve.GiaVe;
                var cancellationFee = CalculateCancellationFee(ve);
                var refundAmount = originalPrice - cancellationFee;

                return Json(new
                {
                    success = true,
                    originalPrice = originalPrice,
                    cancellationFee = cancellationFee,
                    refundAmount = Math.Max(0, refundAmount)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating refund for ticket {VeId}", veId);
                return Json(new { success = false, message = "Có lỗi xảy ra khi tính toán hoàn tiền" });
            }
        }

        // Tính phí hủy vé
        private decimal CalculateCancellationFee(Ve ve)
        {
            if (ve.ChuyenXe == null || ve.NgayHuy == null)
                return 0;

            var hoursUntilDeparture = (ve.ChuyenXe.NgayKhoiHanh - ve.NgayHuy.Value).TotalHours;
            var originalPrice = ve.GiaVe;

            // Quy định phí hủy
            if (hoursUntilDeparture >= 24)
                return originalPrice * 0.1m; // 10% phí hủy
            else if (hoursUntilDeparture >= 12)
                return originalPrice * 0.2m; // 20% phí hủy
            else if (hoursUntilDeparture >= 6)
                return originalPrice * 0.3m; // 30% phí hủy
            else if (hoursUntilDeparture >= 2)
                return originalPrice * 0.5m; // 50% phí hủy
            else
                return originalPrice * 0.8m; // 80% phí hủy
        }

        // ADMIN: Gửi thông báo cho khách hàng
        [HttpPost]
        public async Task<IActionResult> SendTicketNotification(int veId, string subject, string message)
        {
            var ve = await _context.Ves.FindAsync(veId);
            if (ve == null)
                return Json(new { success = false, message = "Không tìm thấy vé." });

            if (string.IsNullOrEmpty(ve.Email))
                return Json(new { success = false, message = "Vé không có địa chỉ email." });

            try
            {
                await _emailService.SendEmailAsync(ve.Email, subject, message);
                return Json(new { success = true, message = "Đã gửi thông báo thành công." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = $"Lỗi gửi email: {ex.Message}" });
            }
        }

        // ADMIN: Gửi thông báo nâng cao (Email + SMS)
        [HttpPost]
        public async Task<IActionResult> SendAdvancedNotification([FromBody] AdvancedNotificationRequest request)
        {
            try
            {
                var ve = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .FirstOrDefaultAsync(v => v.VeId == request.VeId);

                if (ve == null)
                    return Json(new { success = false, message = "Không tìm thấy vé." });

                var results = new List<string>();
                var errors = new List<string>();

                // Send Email
                if (request.SendEmail)
                {
                    if (string.IsNullOrEmpty(ve.Email))
                    {
                        errors.Add("Vé không có địa chỉ email");
                    }
                    else
                    {
                        try
                        {
                            await _emailService.SendEmailAsync(ve.Email, request.Subject, request.Message);
                            results.Add("Email đã được gửi");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending email to {Email}", ve.Email);
                            errors.Add($"Lỗi gửi email: {ex.Message}");
                        }
                    }
                }

                // Send SMS
                if (request.SendSMS)
                {
                    if (string.IsNullOrEmpty(ve.SoDienThoai))
                    {
                        errors.Add("Vé không có số điện thoại");
                    }
                    else
                    {
                        try
                        {
                            // TODO: Implement SMS service
                            // await _smsService.SendSMSAsync(ve.SoDienThoai, request.Message);
                            results.Add("SMS đã được gửi (mô phỏng)");
                            _logger.LogInformation("SMS would be sent to {Phone}: {Message}", ve.SoDienThoai, request.Message);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error sending SMS to {Phone}", ve.SoDienThoai);
                            errors.Add($"Lỗi gửi SMS: {ex.Message}");
                        }
                    }
                }

                // Log notification activity
                _logger.LogInformation("Notification sent for ticket {TicketCode}: Email={SendEmail}, SMS={SendSMS}",
                    ve.MaVe, request.SendEmail, request.SendSMS);

                if (results.Any())
                {
                    var message = string.Join(", ", results);
                    if (errors.Any())
                    {
                        message += ". Lỗi: " + string.Join(", ", errors);
                    }
                    return Json(new { success = true, message = message });
                }
                else
                {
                    return Json(new { success = false, message = "Không thể gửi thông báo: " + string.Join(", ", errors) });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SendAdvancedNotification");
                return Json(new { success = false, message = "Có lỗi xảy ra khi gửi thông báo" });
            }
        }

        // ADMIN: Gửi thông báo hàng loạt
        [HttpPost]
        public async Task<IActionResult> BulkSendNotification([FromBody] BulkNotificationRequest request)
        {
            try
            {
                if (request.VeIds == null || !request.VeIds.Any())
                {
                    return Json(new { success = false, message = "Vui lòng chọn ít nhất một vé" });
                }

                var ves = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.TuyenDuong)
                    .Include(v => v.ChoNgoi)
                    .Where(v => request.VeIds.Contains(v.VeId))
                    .ToListAsync();

                if (!ves.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy vé nào" });
                }

                var successCount = 0;
                var errors = new List<string>();

                foreach (var ve in ves)
                {
                    try
                    {
                        // Replace placeholders in subject and message for each ticket
                        var subject = ReplacePlaceholders(request.Subject, ve);
                        var message = ReplacePlaceholders(request.Message, ve);

                        var ticketErrors = new List<string>();

                        // Send Email
                        if (request.SendEmail)
                        {
                            if (string.IsNullOrEmpty(ve.Email))
                            {
                                ticketErrors.Add("không có email");
                            }
                            else
                            {
                                try
                                {
                                    await _emailService.SendEmailAsync(ve.Email, subject, message);
                                    _logger.LogInformation("Bulk email notification sent for ticket {TicketCode}", ve.MaVe);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error sending bulk email for ticket {TicketCode}", ve.MaVe);
                                    ticketErrors.Add("lỗi gửi email");
                                }
                            }
                        }

                        // Send SMS (placeholder - implement SMS service)
                        if (request.SendSMS)
                        {
                            if (string.IsNullOrEmpty(ve.SoDienThoai))
                            {
                                ticketErrors.Add("không có SĐT");
                            }
                            else
                            {
                                try
                                {
                                    // TODO: Implement SMS service
                                    // await _smsService.SendSmsAsync(ve.SoDienThoai, message);
                                    _logger.LogInformation("Bulk SMS notification would be sent for ticket {TicketCode} to {Phone}",
                                        ve.MaVe, ve.SoDienThoai);
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError(ex, "Error sending bulk SMS for ticket {TicketCode}", ve.MaVe);
                                    ticketErrors.Add("lỗi gửi SMS");
                                }
                            }
                        }

                        if (ticketErrors.Any())
                        {
                            errors.Add($"Vé {ve.MaVe}: {string.Join(", ", ticketErrors)}");
                        }
                        else
                        {
                            successCount++;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing bulk notification for ticket {TicketCode}", ve.MaVe);
                        errors.Add($"Vé {ve.MaVe}: có lỗi xảy ra");
                    }
                }

                var resultMessage = $"Đã gửi thông báo thành công cho {successCount} vé";
                if (errors.Any())
                {
                    resultMessage += $". Có {errors.Count} vé không thể gửi: {string.Join(", ", errors.Take(3))}";
                    if (errors.Count > 3)
                    {
                        resultMessage += "...";
                    }
                }

                return Json(new {
                    success = true,
                    message = resultMessage,
                    successCount = successCount,
                    errorCount = errors.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BulkSendNotification");
                return Json(new { success = false, message = "Có lỗi xảy ra khi gửi thông báo hàng loạt" });
            }
        }



        // ADMIN: Cập nhật trạng thái hàng loạt
        [HttpPost]
        public async Task<IActionResult> BulkUpdateStatus([FromBody] BulkUpdateRequest request)
        {
            try
            {
                if (request.VeIds == null || !request.VeIds.Any())
                {
                    return Json(new { success = false, message = "Vui lòng chọn ít nhất một vé" });
                }

                if (!Enum.TryParse<TrangThaiVe>(request.TrangThai, out var newStatus))
                {
                    return Json(new { success = false, message = "Trạng thái không hợp lệ" });
                }

                var ves = await _context.Ves
                    .Where(v => request.VeIds.Contains(v.VeId))
                    .ToListAsync();

                if (!ves.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy vé nào" });
                }

                var updatedCount = 0;
                var errors = new List<string>();

                foreach (var ve in ves)
                {
                    try
                    {
                        // Validate business rules for each ticket
                        var validationResult = StatusHelper.ValidateStatusChange(ve.VeTrangThai, newStatus);
                        if (!validationResult.IsValid)
                        {
                            errors.Add($"Vé {ve.MaVe}: {validationResult.ErrorMessage}");
                            continue;
                        }

                        var oldStatus = ve.VeTrangThai;
                        ve.VeTrangThai = newStatus;

                        if (!string.IsNullOrWhiteSpace(request.GhiChu))
                        {
                            ve.GhiChu = request.GhiChu.Trim();
                        }

                        // Set specific fields based on status
                        switch (newStatus)
                        {
                            case TrangThaiVe.DaHuy:
                                ve.NgayHuy = DateTime.Now;
                                ve.LyDoHuy = request.GhiChu?.Trim() ?? "Hủy bởi admin";

                                // Send cancellation notification
                                _ = Task.Run(async () => {
                                    try
                                    {
                                        await SendStatusChangeNotification(ve, "cancellation", request.GhiChu);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error sending cancellation notification for ticket {TicketCode}", ve.MaVe);
                                    }
                                });
                                break;

                            case TrangThaiVe.DaSuDung:
                                // Send boarding confirmation notification
                                _ = Task.Run(async () => {
                                    try
                                    {
                                        await SendStatusChangeNotification(ve, "boarding_confirmation", request.GhiChu);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error sending boarding notification for ticket {TicketCode}", ve.MaVe);
                                    }
                                });
                                break;

                            case TrangThaiVe.DaHoanThanh:
                                // Send completion notification
                                _ = Task.Run(async () => {
                                    try
                                    {
                                        await SendStatusChangeNotification(ve, "trip_completed", request.GhiChu);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogError(ex, "Error sending completion notification for ticket {TicketCode}", ve.MaVe);
                                    }
                                });
                                break;
                        }

                        updatedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Error updating ticket {ve.MaVe}");
                        errors.Add($"Vé {ve.MaVe}: Có lỗi xảy ra");
                    }
                }

                await _context.SaveChangesAsync();

                var message = $"Đã cập nhật thành công {updatedCount} vé";
                if (errors.Any())
                {
                    message += $". Có {errors.Count} vé không thể cập nhật: {string.Join(", ", errors.Take(3))}";
                    if (errors.Count > 3)
                    {
                        message += "...";
                    }
                }

                return Json(new {
                    success = true,
                    message = message,
                    updatedCount = updatedCount,
                    errorCount = errors.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in bulk update");
                return Json(new { success = false, message = "Có lỗi xảy ra khi cập nhật hàng loạt" });
            }
        }



        // Helper method to send automatic status change notifications
        private async Task SendStatusChangeNotification(Ve ve, string notificationType, string? additionalNote = null)
        {
            try
            {
                if (string.IsNullOrEmpty(ve.Email))
                    return;

                var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                               (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");

                string subject = "";
                string message = "";

                switch (notificationType)
                {
                    case "cancellation":
                        subject = $"Thông báo hủy vé - {ve.MaVe}";
                        message = $@"Kính chào {ve.TenKhach},

Chúng tôi xin thông báo vé {ve.MaVe} đã được hủy.

Thông tin vé đã hủy:
- Tuyến đường: {tuyenDuong}
- Ngày khởi hành: {ve.ChuyenXe?.NgayKhoiHanh:dd/MM/yyyy HH:mm}
- Số ghế: {ve.ChoNgoi?.SoGhe}
- Lý do hủy: {ve.LyDoHuy}

{(string.IsNullOrEmpty(additionalNote) ? "" : $"Ghi chú: {additionalNote}\n")}
Nếu có bất kỳ thắc mắc nào, vui lòng liên hệ hotline để được hỗ trợ.

Trân trọng,
Đội ngũ hỗ trợ khách hàng";
                        break;

                    case "boarding_confirmation":
                        subject = $"Xác nhận lên xe - Vé {ve.MaVe}";
                        message = $@"Kính chào {ve.TenKhach},

Chúng tôi xác nhận quý khách đã lên xe thành công.

Thông tin chuyến đi:
- Mã vé: {ve.MaVe}
- Tuyến đường: {tuyenDuong}
- Ngày khởi hành: {ve.ChuyenXe?.NgayKhoiHanh:dd/MM/yyyy HH:mm}
- Số ghế: {ve.ChoNgoi?.SoGhe}

{(string.IsNullOrEmpty(additionalNote) ? "" : $"Ghi chú: {additionalNote}\n")}
Chúc quý khách có chuyến đi an toàn và vui vẻ!

Trân trọng,
Đội ngũ hỗ trợ khách hàng";
                        break;

                    case "trip_completed":
                        subject = $"Hoàn thành chuyến đi - Vé {ve.MaVe}";
                        message = $@"Kính chào {ve.TenKhach},

Cảm ơn quý khách đã sử dụng dịch vụ của chúng tôi.

Thông tin chuyến đi đã hoàn thành:
- Mã vé: {ve.MaVe}
- Tuyến đường: {tuyenDuong}
- Ngày khởi hành: {ve.ChuyenXe?.NgayKhoiHanh:dd/MM/yyyy HH:mm}
- Số ghế: {ve.ChoNgoi?.SoGhe}

{(string.IsNullOrEmpty(additionalNote) ? "" : $"Ghi chú: {additionalNote}\n")}
Hy vọng quý khách hài lòng với dịch vụ của chúng tôi. Đánh giá của quý khách sẽ giúp chúng tôi cải thiện chất lượng dịch vụ.

Trân trọng,
Đội ngũ hỗ trợ khách hàng";
                        break;

                    default:
                        return;
                }

                await _emailService.SendEmailAsync(ve.Email, subject, message);
                _logger.LogInformation("Status change notification sent for ticket {TicketCode}, type: {NotificationType}",
                    ve.MaVe, notificationType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending status change notification for ticket {TicketCode}, type: {NotificationType}",
                    ve.MaVe, notificationType);
            }
        }



        // Helper methods for export
        private string GenerateExportFileName(string? searchString, int? chuyenXeId, string? trangThaiVe, DateTime? tuNgay, DateTime? denNgay, string format)
        {
            var fileName = "DanhSachVe";
            var filters = new List<string>();

            if (!string.IsNullOrEmpty(searchString))
                filters.Add($"Tim_{searchString}");
            if (chuyenXeId.HasValue)
                filters.Add($"CX_{chuyenXeId}");
            if (!string.IsNullOrEmpty(trangThaiVe))
                filters.Add($"TT_{trangThaiVe}");
            if (tuNgay.HasValue)
                filters.Add($"Tu_{tuNgay.Value:yyyyMMdd}");
            if (denNgay.HasValue)
                filters.Add($"Den_{denNgay.Value:yyyyMMdd}");

            if (filters.Any())
                fileName += "_" + string.Join("_", filters);

            fileName += $"_{DateTime.Now:yyyyMMddHHmmss}";
            fileName += format.ToLower() == "excel" ? ".xlsx" : ".csv";

            return fileName;
        }

        private async Task<IActionResult> ExportToExcel(List<Ve> ves, string fileName)
        {
            using (var package = new OfficeOpenXml.ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("DanhSachVe");

                // Headers
                ws.Cells[1, 1].Value = "Mã vé";
                ws.Cells[1, 2].Value = "Tên khách";
                ws.Cells[1, 3].Value = "Email";
                ws.Cells[1, 4].Value = "Số điện thoại";
                ws.Cells[1, 5].Value = "Tuyến đường";
                ws.Cells[1, 6].Value = "Ngày khởi hành";
                ws.Cells[1, 7].Value = "Giờ khởi hành";
                ws.Cells[1, 8].Value = "Biển số xe";
                ws.Cells[1, 9].Value = "Số ghế";
                ws.Cells[1, 10].Value = "Giá vé";
                ws.Cells[1, 11].Value = "Trạng thái thanh toán";
                ws.Cells[1, 12].Value = "Trạng thái vé";
                ws.Cells[1, 13].Value = "Ngày đặt";
                ws.Cells[1, 14].Value = "Ghi chú";

                // Style headers
                using (var range = ws.Cells[1, 1, 1, 14])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = OfficeOpenXml.Style.ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.LightGray);
                }

                // Data
                int row = 2;
                foreach (var ve in ves)
                {
                    var tenKhach = ve.TenKhach ?? "";
                    var email = ve.Email ?? "";
                    var soDienThoai = ve.SoDienThoai ?? "";
                    var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                                   (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");
                    var ngayKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy") ?? "";
                    var gioKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("HH:mm") ?? "";
                    var bienSoXe = ve.ChuyenXe?.Xe?.BienSoXe ?? "";
                    var soGhe = ve.ChoNgoi?.SoGhe ?? "";
                    var trangThaiThanhToan = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true
                        ? "Đã thanh toán" : "Chưa thanh toán";

                    ws.Cells[row, 1].Value = ve.MaVe;
                    ws.Cells[row, 2].Value = tenKhach;
                    ws.Cells[row, 3].Value = email;
                    ws.Cells[row, 4].Value = soDienThoai;
                    ws.Cells[row, 5].Value = tuyenDuong;
                    ws.Cells[row, 6].Value = ngayKhoiHanh;
                    ws.Cells[row, 7].Value = gioKhoiHanh;
                    ws.Cells[row, 8].Value = bienSoXe;
                    ws.Cells[row, 9].Value = soGhe;
                    ws.Cells[row, 10].Value = ve.GiaVe;
                    ws.Cells[row, 11].Value = trangThaiThanhToan;
                    ws.Cells[row, 12].Value = StatusHelper.GetStatusText(ve.VeTrangThai);
                    ws.Cells[row, 13].Value = ve.NgayDat.ToString("dd/MM/yyyy HH:mm");
                    ws.Cells[row, 14].Value = ve.GhiChu ?? "";
                    row++;
                }

                ws.Cells[ws.Dimension.Address].AutoFitColumns();
                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", fileName);
            }
        }

        private IActionResult ExportToCsv(List<Ve> ves, string fileName)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Mã vé,Tên khách,Email,Số điện thoại,Tuyến đường,Ngày khởi hành,Giờ khởi hành,Biển số xe,Số ghế,Giá vé,Trạng thái thanh toán,Trạng thái vé,Ngày đặt,Ghi chú");

            foreach (var ve in ves)
            {
                var tenKhach = ve.TenKhach ?? "";
                var email = ve.Email ?? "";
                var soDienThoai = ve.SoDienThoai ?? "";
                var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                               (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");
                var ngayKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy") ?? "";
                var gioKhoiHanh = ve.ChuyenXe?.NgayKhoiHanh.ToString("HH:mm") ?? "";
                var bienSoXe = ve.ChuyenXe?.Xe?.BienSoXe ?? "";
                var soGhe = ve.ChoNgoi?.SoGhe ?? "";
                var trangThaiThanhToan = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true
                    ? "Đã thanh toán" : "Chưa thanh toán";

                csv.AppendLine($"\"{ve.MaVe}\"," +
                              $"\"{tenKhach}\"," +
                              $"\"{email}\"," +
                              $"\"{soDienThoai}\"," +
                              $"\"{tuyenDuong}\"," +
                              $"\"{ngayKhoiHanh}\"," +
                              $"\"{gioKhoiHanh}\"," +
                              $"\"{bienSoXe}\"," +
                              $"\"{soGhe}\"," +
                              $"\"{ve.GiaVe:N0}\"," +
                              $"\"{trangThaiThanhToan}\"," +
                              $"\"{StatusHelper.GetStatusText(ve.VeTrangThai)}\"," +
                              $"\"{ve.NgayDat:dd/MM/yyyy HH:mm}\"," +
                              $"\"{ve.GhiChu ?? ""}\"");
            }

            var bytes = System.Text.Encoding.UTF8.GetBytes(csv.ToString());
            return File(bytes, "text/csv", fileName);
        }

        // ADMIN: In vé hàng loạt
        [HttpPost]
        public async Task<IActionResult> BulkPrintTickets([FromBody] BulkPrintRequest request)
        {
            try
            {
                if (request.VeIds == null || !request.VeIds.Any())
                {
                    return Json(new { success = false, message = "Vui lòng chọn ít nhất một vé" });
                }

                var ves = await _context.Ves
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.TuyenDuong)
                    .Include(v => v.ChuyenXe)
                        .ThenInclude(cx => cx.Xe)
                    .Include(v => v.ChoNgoi)
                    .Include(v => v.ThanhToans)
                    .Where(v => request.VeIds.Contains(v.VeId))
                    .ToListAsync();

                if (!ves.Any())
                {
                    return Json(new { success = false, message = "Không tìm thấy vé nào" });
                }

                // Generate HTML for all tickets
                var htmlContent = GenerateBulkTicketHtml(ves);

                // Return HTML content for printing
                return Json(new {
                    success = true,
                    html = htmlContent,
                    count = ves.Count,
                    message = $"Đã chuẩn bị {ves.Count} vé để in"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in BulkPrintTickets");
                return Json(new { success = false, message = "Có lỗi xảy ra khi chuẩn bị in vé hàng loạt" });
            }
        }

        // Helper method to generate HTML for bulk ticket printing
        private string GenerateBulkTicketHtml(List<Ve> ves)
        {
            var html = @"
<!DOCTYPE html>
<html>
<head>
    <meta charset='utf-8'>
    <title>In vé hàng loạt</title>
    <style>
        @page {
            size: A4;
            margin: 10mm;
        }

        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            margin: 0;
            padding: 0;
            background: white;
        }

        .ticket-container {
            width: 100%;
            margin-bottom: 20mm;
            page-break-after: auto;
            border: 2px solid #007bff;
            border-radius: 10px;
            padding: 15px;
            background: linear-gradient(135deg, #f8f9fa 0%, #ffffff 100%);
        }

        .ticket-header {
            text-align: center;
            border-bottom: 2px dashed #007bff;
            padding-bottom: 15px;
            margin-bottom: 15px;
        }

        .company-name {
            font-size: 24px;
            font-weight: bold;
            color: #007bff;
            margin-bottom: 5px;
        }

        .ticket-title {
            font-size: 18px;
            color: #6c757d;
            margin-bottom: 10px;
        }

        .ticket-code {
            font-size: 16px;
            font-weight: bold;
            color: #dc3545;
            background: #fff;
            padding: 5px 15px;
            border-radius: 20px;
            border: 1px solid #dc3545;
            display: inline-block;
        }

        .ticket-body {
            display: flex;
            justify-content: space-between;
            margin-bottom: 15px;
        }

        .ticket-left, .ticket-right {
            width: 48%;
        }

        .info-row {
            display: flex;
            justify-content: space-between;
            margin-bottom: 8px;
            padding: 5px 0;
            border-bottom: 1px dotted #dee2e6;
        }

        .info-label {
            font-weight: bold;
            color: #495057;
            width: 40%;
        }

        .info-value {
            color: #212529;
            width: 60%;
            text-align: right;
        }

        .route-info {
            text-align: center;
            background: #e3f2fd;
            padding: 10px;
            border-radius: 8px;
            margin: 15px 0;
            border: 1px solid #bbdefb;
        }

        .route-text {
            font-size: 18px;
            font-weight: bold;
            color: #1976d2;
        }

        .ticket-footer {
            border-top: 2px dashed #007bff;
            padding-top: 15px;
            text-align: center;
        }

        .footer-note {
            font-size: 12px;
            color: #6c757d;
            margin-bottom: 3px;
        }

        .qr-placeholder {
            width: 80px;
            height: 80px;
            border: 2px dashed #6c757d;
            margin: 10px auto;
            display: flex;
            align-items: center;
            justify-content: center;
            font-size: 10px;
            color: #6c757d;
        }

        @media print {
            .ticket-container {
                page-break-inside: avoid;
                margin-bottom: 15mm;
            }
        }
    </style>
</head>
<body>";

            foreach (var ve in ves)
            {
                var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                               (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");
                var bienSoXe = ve.ChuyenXe?.Xe?.BienSoXe ?? "";
                var soGhe = ve.ChoNgoi?.SoGhe ?? "";
                var trangThaiThanhToan = ve.ThanhToans?.Any(t => t.TrangThai == TrangThaiThanhToan.ThanhCong) == true
                    ? "Đã thanh toán" : "Chưa thanh toán";

                html += $@"
    <div class='ticket-container'>
        <div class='ticket-header'>
            <div class='company-name'>HỆ THỐNG ĐẶT VÉ XE</div>
            <div class='ticket-title'>VÉ XE KHÁCH</div>
            <div class='ticket-code'>Mã vé: {ve.MaVe}</div>
        </div>

        <div class='route-info'>
            <div class='route-text'>{tuyenDuong}</div>
        </div>

        <div class='ticket-body'>
            <div class='ticket-left'>
                <div class='info-row'>
                    <span class='info-label'>Tên khách:</span>
                    <span class='info-value'>{ve.TenKhach}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Số điện thoại:</span>
                    <span class='info-value'>{ve.SoDienThoai}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Email:</span>
                    <span class='info-value'>{ve.Email ?? "N/A"}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Số ghế:</span>
                    <span class='info-value'>{soGhe}</span>
                </div>
            </div>

            <div class='ticket-right'>
                <div class='info-row'>
                    <span class='info-label'>Ngày khởi hành:</span>
                    <span class='info-value'>{ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy") ?? ""}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Giờ khởi hành:</span>
                    <span class='info-value'>{ve.ChuyenXe?.NgayKhoiHanh.ToString("HH:mm") ?? ""}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Biển số xe:</span>
                    <span class='info-value'>{bienSoXe}</span>
                </div>
                <div class='info-row'>
                    <span class='info-label'>Giá vé:</span>
                    <span class='info-value'>{ve.GiaVe:N0} VNĐ</span>
                </div>
            </div>
        </div>

        <div class='info-row'>
            <span class='info-label'>Trạng thái:</span>
            <span class='info-value'>{StatusHelper.GetStatusText(ve.VeTrangThai)} - {trangThaiThanhToan}</span>
        </div>

        <div class='info-row'>
            <span class='info-label'>Ngày đặt:</span>
            <span class='info-value'>{ve.NgayDat:dd/MM/yyyy HH:mm}</span>
        </div>

        <div class='qr-placeholder'>
            QR CODE<br>{ve.MaVe}
        </div>

        <div class='ticket-footer'>
            <div class='footer-note'>
                <strong>Lưu ý quan trọng:</strong>
            </div>
            <div class='footer-note'>
                • Vui lòng có mặt tại bến xe trước giờ khởi hành 15 phút
            </div>
            <div class='footer-note'>
                • Mang theo giấy tờ tùy thân khi đi xe
            </div>
            <div class='footer-note'>
                • Liên hệ hotline để được hỗ trợ: 1900-xxxx
            </div>
            <div class='footer-note' style='margin-top: 15px; font-size: 0.8em;'>
                Vé được in lúc: {DateTime.Now:dd/MM/yyyy HH:mm}
            </div>
        </div>
    </div>";
            }

            html += @"
</body>
</html>";

            return html;
        }

        // Helper method to replace placeholders in notification messages
        private string ReplacePlaceholders(string text, Ve ve)
        {
            if (string.IsNullOrEmpty(text) || ve == null)
                return text;

            var tuyenDuong = ve.ChuyenXe?.TuyenDuong?.TenTuyen ??
                           (ve.ChuyenXe != null ? $"{ve.ChuyenXe.DiemDi} - {ve.ChuyenXe.DiemDen}" : "");

            return text
                .Replace("{TenKhach}", ve.TenKhach ?? "")
                .Replace("{MaVe}", ve.MaVe ?? "")
                .Replace("{SoDienThoai}", ve.SoDienThoai ?? "")
                .Replace("{Email}", ve.Email ?? "")
                .Replace("{TuyenDuong}", tuyenDuong)
                .Replace("{NgayKhoiHanh}", ve.ChuyenXe?.NgayKhoiHanh.ToString("dd/MM/yyyy HH:mm") ?? "")
                .Replace("{SoGhe}", ve.ChoNgoi?.SoGhe ?? "")
                .Replace("{GiaVe}", ve.GiaVe.ToString("N0") + " VNĐ")
                .Replace("{TrangThaiVe}", StatusHelper.GetStatusText(ve.VeTrangThai))
                .Replace("{NgayDat}", ve.NgayDat.ToString("dd/MM/yyyy HH:mm"))
                .Replace("{BienSoXe}", ve.ChuyenXe?.Xe?.BienSoXe ?? "");
        }
    }

    // Request model for bulk update
    public class BulkUpdateRequest
    {
        public List<int> VeIds { get; set; } = new List<int>();
        public string TrangThai { get; set; } = string.Empty;
        public string? GhiChu { get; set; }
    }

    // Request model for marking ticket as used via QR
    public class MarkTicketUsedRequest
    {
        public string QrData { get; set; } = string.Empty;
        public string? GhiChu { get; set; }
    }

    // Request model for advanced notification
    public class AdvancedNotificationRequest
    {
        public int VeId { get; set; }
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
        public bool SendSMS { get; set; }
    }

    // Request model for bulk notification
    public class BulkNotificationRequest
    {
        public List<int> VeIds { get; set; } = new List<int>();
        public string Subject { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public bool SendEmail { get; set; }
        public bool SendSMS { get; set; }
    }

    // Request model for bulk print
    public class BulkPrintRequest
    {
        public List<int> VeIds { get; set; } = new List<int>();
    }


}
