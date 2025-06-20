using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;

namespace DatVeXe.Controllers
{
    public class ContactsController : Controller
    {
        private readonly DatVeXeContext _context;

        public ContactsController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Contacts
        public async Task<IActionResult> Index(string? searchTerm)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var query = _context.DanhBaHanhKhachs
                .Where(d => d.NguoiDungId == userId.Value);

            if (!string.IsNullOrEmpty(searchTerm))
            {
                query = query.Where(d => 
                    d.TenHanhKhach.Contains(searchTerm) ||
                    d.SoDienThoai.Contains(searchTerm) ||
                    (d.Email != null && d.Email.Contains(searchTerm))
                );
            }

            var contacts = await query
                .OrderByDescending(d => d.LanSuDungCuoi ?? d.NgayTao)
                .ThenBy(d => d.TenHanhKhach)
                .ToListAsync();

            var viewModel = new ContactListViewModel
            {
                DanhSachLienHe = contacts,
                SearchTerm = searchTerm,
                TotalContacts = contacts.Count
            };

            return View(viewModel);
        }

        // GET: Contacts/Details/5
        public async Task<IActionResult> Details(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var contact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (contact == null)
            {
                return NotFound();
            }

            return View(contact);
        }

        // GET: Contacts/Create
        public IActionResult Create()
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        // POST: Contacts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DanhBaHanhKhach model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            model.NguoiDungId = userId.Value;

            // Kiểm tra trùng lặp số điện thoại
            var existingContact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.NguoiDungId == userId.Value && d.SoDienThoai == model.SoDienThoai);

            if (existingContact != null)
            {
                ModelState.AddModelError("SoDienThoai", "Số điện thoại này đã có trong danh bạ.");
            }

            if (ModelState.IsValid)
            {
                model.NgayTao = DateTime.Now;
                _context.DanhBaHanhKhachs.Add(model);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Đã thêm liên hệ vào danh bạ thành công!";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // GET: Contacts/Edit/5
        public async Task<IActionResult> Edit(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var contact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (contact == null)
            {
                return NotFound();
            }

            return View(contact);
        }

        // POST: Contacts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DanhBaHanhKhach model)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            if (id != model.DanhBaId)
            {
                return NotFound();
            }

            var existingContact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (existingContact == null)
            {
                return NotFound();
            }

            // Kiểm tra trùng lặp số điện thoại (trừ chính nó)
            var duplicateContact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.NguoiDungId == userId.Value && 
                                        d.SoDienThoai == model.SoDienThoai && 
                                        d.DanhBaId != id);

            if (duplicateContact != null)
            {
                ModelState.AddModelError("SoDienThoai", "Số điện thoại này đã có trong danh bạ.");
            }

            if (ModelState.IsValid)
            {
                existingContact.TenHanhKhach = model.TenHanhKhach;
                existingContact.SoDienThoai = model.SoDienThoai;
                existingContact.Email = model.Email;
                existingContact.DiaChi = model.DiaChi;
                existingContact.NgaySinh = model.NgaySinh;
                existingContact.GioiTinh = model.GioiTinh;
                existingContact.CCCD = model.CCCD;
                existingContact.GhiChu = model.GhiChu;

                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Đã cập nhật thông tin liên hệ thành công!";
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        // GET: Contacts/Delete/5
        public async Task<IActionResult> Delete(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var contact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (contact == null)
            {
                return NotFound();
            }

            return View(contact);
        }

        // POST: Contacts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return RedirectToAction("Login", "Account");
            }

            var contact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (contact != null)
            {
                _context.DanhBaHanhKhachs.Remove(contact);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Đã xóa liên hệ khỏi danh bạ thành công!";
            }

            return RedirectToAction(nameof(Index));
        }

        // API: Contacts/GetContacts - Lấy danh sách liên hệ cho autocomplete
        [HttpGet]
        public async Task<IActionResult> GetContacts(string term = "")
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return Json(new List<object>());
            }

            var query = _context.DanhBaHanhKhachs
                .Where(d => d.NguoiDungId == userId.Value);

            if (!string.IsNullOrEmpty(term))
            {
                query = query.Where(d => 
                    d.TenHanhKhach.Contains(term) ||
                    d.SoDienThoai.Contains(term)
                );
            }

            var contacts = await query
                .OrderByDescending(d => d.SoLanSuDung)
                .ThenBy(d => d.TenHanhKhach)
                .Take(10)
                .Select(d => new
                {
                    id = d.DanhBaId,
                    tenHanhKhach = d.TenHanhKhach,
                    soDienThoai = d.SoDienThoai,
                    email = d.Email,
                    ngaySinh = d.NgaySinh,
                    gioiTinh = d.GioiTinh,
                    cccd = d.CCCD,
                    diaChi = d.DiaChi
                })
                .ToListAsync();

            return Json(contacts);
        }

        // POST: Contacts/UpdateUsage/5 - Cập nhật lần sử dụng cuối
        [HttpPost]
        public async Task<IActionResult> UpdateUsage(int id)
        {
            var userId = HttpContext.Session.GetInt32("UserId");
            if (!userId.HasValue)
            {
                return Json(new { success = false });
            }

            var contact = await _context.DanhBaHanhKhachs
                .FirstOrDefaultAsync(d => d.DanhBaId == id && d.NguoiDungId == userId.Value);

            if (contact != null)
            {
                contact.LanSuDungCuoi = DateTime.Now;
                contact.SoLanSuDung++;
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }

            return Json(new { success = false });
        }
    }
}
