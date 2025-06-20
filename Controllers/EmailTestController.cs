using DatVeXe.Services;
using Microsoft.AspNetCore.Mvc;

namespace DatVeXe.Controllers
{
    public class EmailTestController : Controller
    {
        private readonly IEmailService _emailService;

        public EmailTestController(IEmailService emailService)
        {
            _emailService = emailService;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> SendTestEmail(string email, string type)
        {
            if (string.IsNullOrEmpty(email))
            {
                TempData["Error"] = "Vui lòng nhập email";
                return View("Index");
            }

            try
            {
                bool result = false;
                string emailContent = "";

                if (type == "confirmation")
                {
                    result = await _emailService.SendTicketConfirmationAsync(
                        email,
                        "Nguyễn Văn Test",
                        "VE20250605001",
                        "TP.HCM - Nha Trang",
                        "Ghế A01",
                        350000,
                        DateTime.Now.AddDays(1)
                    );

                    emailContent = GenerateConfirmationEmailDemo(email, "Nguyễn Văn Test", "VE20250605001",
                        "TP.HCM - Nha Trang", "Ghế A01", 350000, DateTime.Now.AddDays(1));
                }
                else if (type == "cancellation")
                {
                    result = await _emailService.SendTicketCancellationAsync(
                        email,
                        "Nguyễn Văn Test",
                        "VE20250605001",
                        "TP.HCM - Nha Trang",
                        "Hủy theo yêu cầu của khách hàng"
                    );

                    emailContent = GenerateCancellationEmailDemo(email, "Nguyễn Văn Test", "VE20250605001",
                        "TP.HCM - Nha Trang", "Hủy theo yêu cầu của khách hàng");
                }

                if (result)
                {
                    TempData["EmailContent"] = emailContent;
                    TempData["Success"] = $"✅ Email {type} đã được gửi thành công đến {email}! (Demo mode - xem nội dung bên dưới)";
                }
                else
                {
                    TempData["Error"] = "❌ Gửi email thất bại. Vui lòng kiểm tra cấu hình email.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi: {ex.Message}";
            }

            return View("Index");
        }

        private string GenerateConfirmationEmailDemo(string email, string customerName, string ticketCode,
            string tripInfo, string seatInfo, decimal price, DateTime departureTime)
        {
            return $@"
📧 EMAIL XÁC NHẬN ĐẶT VÉ
========================

Gửi đến: {email}
Chủ đề: Xác nhận đặt vé thành công - Mã vé: {ticketCode}

Xin chào {customerName},

Cảm ơn bạn đã sử dụng dịch vụ của chúng tôi. Vé của bạn đã được đặt thành công!

🎫 THÔNG TIN VÉ:
- Mã vé: {ticketCode}
- Tuyến đường: {tripInfo}
- Chỗ ngồi: {seatInfo}
- Thời gian khởi hành: {departureTime:dd/MM/yyyy HH:mm}
- Tổng tiền: {price:N0} VNĐ

📱 LƯU Ý QUAN TRỌNG:
- Vui lòng mang theo email này hoặc mã vé {ticketCode} khi lên xe
- Có mặt tại bến xe trước giờ khởi hành ít nhất 15 phút

📞 LIÊN HỆ HỖ TRỢ:
- Hotline: 1900-xxxx
- Email: support@datvexe.com
- Website: www.datvexe.com

Cảm ơn bạn đã tin tưởng và sử dụng dịch vụ của chúng tôi!
";
        }

        private string GenerateCancellationEmailDemo(string email, string customerName, string ticketCode,
            string tripInfo, string reason)
        {
            return $@"
📧 EMAIL THÔNG BÁO HỦY VÉ
========================

Gửi đến: {email}
Chủ đề: Thông báo hủy vé - Mã vé: {ticketCode}

Xin chào {customerName},

Chúng tôi xin thông báo vé của bạn đã được hủy.

🎫 THÔNG TIN VÉ ĐÃ HỦY:
- Mã vé: {ticketCode}
- Tuyến đường: {tripInfo}
- Lý do hủy: {reason}
- Thời gian hủy: {DateTime.Now:dd/MM/yyyy HH:mm}

⚠️ LƯU Ý:
- Nếu bạn đã thanh toán, chúng tôi sẽ tiến hành hoàn tiền theo chính sách của công ty
- Thời gian hoàn tiền: 3-7 ngày làm việc

📞 LIÊN HỆ HỖ TRỢ:
- Hotline: 1900-xxxx
- Email: support@datvexe.com
- Website: www.datvexe.com

Cảm ơn bạn đã sử dụng dịch vụ của chúng tôi!
";
        }
    }
}
