using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DatVeXe.Models;
using Microsoft.AspNetCore.Authorization;

namespace DatVeXe.Areas.Admin.Controllers
{
    [Area("Admin")]
    [Authorize(Roles = "Admin")]
    public class XeController : Controller
    {
        private readonly DatVeXeContext _context;

        public XeController(DatVeXeContext context)
        {
            _context = context;
        }

        // GET: Admin/Xe
        public async Task<IActionResult> Index()
        {
            var xes = await _context.Xes.ToListAsync();
            return View(xes);
        }

        // GET: Admin/Xe/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.FirstOrDefaultAsync(m => m.XeId == id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // GET: Admin/Xe/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Admin/Xe/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("BienSoXe,LoaiXe,SoGhe,MoTa")] Xe xe)
        {
            if (ModelState.IsValid)
            {
                _context.Add(xe);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            return View(xe);
        }

        // GET: Admin/Xe/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.FindAsync(id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // POST: Admin/Xe/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("XeId,BienSoXe,LoaiXe,SoGhe,MoTa")] Xe xe)
        {
            if (id != xe.XeId) return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(xe);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!XeExists(xe.XeId)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            return View(xe);
        }

        // GET: Admin/Xe/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var xe = await _context.Xes.FirstOrDefaultAsync(m => m.XeId == id);
            if (xe == null) return NotFound();
            return View(xe);
        }

        // POST: Admin/Xe/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var xe = await _context.Xes.FindAsync(id);
            if (xe != null)
            {
                _context.Xes.Remove(xe);
                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        private bool XeExists(int id)
        {
            return _context.Xes.Any(e => e.XeId == id);
        }
    }
}
// Đã chuyển toàn bộ logic sang QuanLyXeController, file này nên được xóa để tránh trùng lặp.
