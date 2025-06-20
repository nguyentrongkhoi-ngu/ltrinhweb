using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DatVeXe.Attributes
{
    public class AdminAuthorizationAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var session = context.HttpContext.Session;
            var isAdmin = session.GetInt32("IsAdmin");
            var userId = session.GetInt32("UserId");

            // Kiểm tra nếu chưa đăng nhập hoặc không phải admin
            if (userId == null || isAdmin != 1)
            {
                // Lưu thông báo lỗi vào TempData thông qua Controller
                var controller = context.Controller as Controller;
                if (controller != null)
                {
                    controller.TempData["ErrorTitle"] = "Truy cập bị từ chối";
                    controller.TempData["ErrorMessage"] = "Bạn không có quyền truy cập vào khu vực quản trị. Chỉ có quản trị viên mới được phép truy cập.";
                    controller.TempData["ErrorType"] = "Unauthorized";
                }

                // Chuyển hướng về trang chủ
                context.Result = new RedirectToActionResult("Index", "Home", new { area = "" });
                return;
            }

            base.OnActionExecuting(context);
        }
    }
}
