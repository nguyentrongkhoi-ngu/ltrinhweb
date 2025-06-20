using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using System.Security.Cryptography;
using System.Text;

namespace DatVeXe.Controllers
{
    public class ResetPasswordController : Controller
    {
        private readonly DatVeXeContext _context;

        public ResetPasswordController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: /ResetPassword/Admin - Reset admin password to plain text
        public async Task<IActionResult> Admin()
        {
            try
            {
                var admin = await _context.NguoiDungs
                    .FirstOrDefaultAsync(u => u.Email == "admin@datvexe.com");

                if (admin != null)
                {
                    // Set plain text password that will be auto-hashed on next login
                    admin.MatKhau = "admin123";
                    await _context.SaveChangesAsync();
                    
                    return Json(new { 
                        success = true, 
                        message = "Admin password reset to 'admin123'. Please login with admin@datvexe.com / admin123" 
                    });
                }
                else
                {
                    return Json(new { 
                        success = false, 
                        message = "Admin user not found" 
                    });
                }
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = "Error: " + ex.Message 
                });
            }
        }

        // GET: /ResetPassword/Hash - Show current password hashes
        public async Task<IActionResult> Hash()
        {
            try
            {
                var users = await _context.NguoiDungs
                    .Select(u => new { 
                        u.Email, 
                        u.MatKhau, 
                        u.LaAdmin,
                        PlainTextHash_admin123 = HashPassword("admin123"),
                        PlainTextHash_Admin123 = HashPassword("Admin@123")
                    })
                    .ToListAsync();

                return Json(users);
            }
            catch (Exception ex)
            {
                return Json(new { 
                    success = false, 
                    message = "Error: " + ex.Message 
                });
            }
        }

        private string HashPassword(string password)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
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
