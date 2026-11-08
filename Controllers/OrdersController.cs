using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using backend.Data;
using backend.Models;
using backend.Services;

namespace backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly EmailService _emailService;
        private readonly ILogger<OrdersController> _logger;

        public OrdersController(AppDbContext context, EmailService emailService, ILogger<OrdersController> logger)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] Order order)
        {
            // 1. Order database mein save karein
            try
            {
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save order for {CustomerEmail}.", order.Email);
                return StatusCode(StatusCodes.Status500InternalServerError,
                    new { message = "Order could not be placed. Please try again." });
            }

            // 2. Emails bhejein. Email fail ho jaye to bhi order place ho chuka hai,
            //    is liye error sirf log hota hai aur HTTP 200 hi return hota hai.
            try
            {
                await _emailService.SendOrderEmailsAsync(
                    order.CustomerName,
                    order.Email,
                    order.Phone,
                    order.Address,
                    order.City,
                    order.TotalAmount,
                    order.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Order {OrderId} saved but email notification failed.", order.Id);
            }

            return Ok(new { message = "Order placed successfully!", orderId = order.Id });
        }
    }
}
