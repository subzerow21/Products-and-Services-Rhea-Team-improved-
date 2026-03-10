using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyAspNetApp.Data;
using MyAspNetApp.Models;
using MyAspNetApp.Services;
using System.Text.Json;

namespace MyAspNetApp.Controllers
{
    public class HomeController : Controller
    {
        private readonly AppDbContext _db;
        private readonly NotificationService _notifications;
        private readonly EmailService _emailService;
        private readonly ILogger<HomeController> _logger;

        public HomeController(AppDbContext db, NotificationService notifications,
            EmailService emailService, ILogger<HomeController> logger)
        {
            _db = db;
            _notifications = notifications;
            _emailService = emailService;
            _logger = logger;
        }

        // GET: /
        public IActionResult Index() => View();

        // GET: /Shop
        public IActionResult Shop() => View();

        // GET: /Home/About
        public IActionResult About() => View();

        // GET: /Home/Product?id=5
        public IActionResult Product(int id)
        {
            ViewData["ProductId"] = id;
            return View();
        }

        // GET: /Home/Contact
        public IActionResult Contact() => View();

        // GET: /Home/Reviews?id=5
        public IActionResult Reviews(int id)
        {
            ViewData["ProductId"] = id;
            return View();
        }

        // GET: /Home/WriteReview?id=5
        public IActionResult WriteReview(int id)
        {
            ViewData["ProductId"] = id;
            return View();
        }

        // GET: /Home/Cart
        public IActionResult Cart() => View();

        // GET: /Home/Checkout
        public async Task<IActionResult> Checkout()
        {
            if (!ProductData.Cart.Any())
                return RedirectToAction("Cart");

            var vm = await BuildCheckoutVmAsync();
            return View(vm);
        }

        // POST: /Home/PlaceOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(CheckoutViewModel vm)
        {
            vm.CartItems = await GetCheckoutItemsAsync();
            vm.Subtotal = vm.CartItems.Sum(i => i.Price * i.Quantity);
            vm.ShippingFee = vm.DeliveryOption == "Express" ? 300m : 150m;

            ModelState.Remove("CartItems");
            ModelState.Remove("Subtotal");
            ModelState.Remove("ShippingFee");
            ModelState.Remove("CardNumber");
            ModelState.Remove("CardExpiry");
            ModelState.Remove("CardCvv");

            if (!ModelState.IsValid)
                return View("Checkout", vm);

            var estimatedDelivery = DateTime.Now.AddDays(vm.DeliveryOption == "Express" ? 2 : 5);

            // ── Save order header to dbo.Orders ──
            var dbOrder = new DbOrder
            {
                UserID = "guest",
                OrderDate = DateTime.Now,
                Quantity = vm.CartItems.Sum(i => i.Quantity),
                SubTotal = vm.Subtotal,
                ShippingFee = vm.ShippingFee,
                TotalAmount = vm.Subtotal + vm.ShippingFee,
                Status = "Placed",
                FullName = vm.FullName,
                Email = vm.Email,
                PhoneNumber = vm.Phone,
                StreetAddress = vm.Address,
                City = vm.City,
                PostalCode = vm.PostalCode,
                DeliveryOption = vm.DeliveryOption,
                PaymentMethod = vm.PaymentMethod,
                EstimatedDeliveryDate = estimatedDelivery
            };

            _db.Orders.Add(dbOrder);
            await _db.SaveChangesAsync();   // generates OrderID

            // ── Save order lines to dbo.OrderItems ──
            foreach (var item in vm.CartItems)
            {
                _db.OrderItems.Add(new DbOrderItem
                {
                    OrderID = dbOrder.OrderID,
                    ProductId = item.ProductId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price,
                    Size = item.Size
                });
            }
            await _db.SaveChangesAsync();

            ProductData.Cart.Clear();

            _notifications.AddNotification("guest",
                $"Your order #{dbOrder.OrderID} has been successfully confirmed.",
                NotificationType.Success);

            // ── Send order confirmation email (best-effort; failure does not affect order) ──
            if (!string.IsNullOrEmpty(dbOrder.Email))
            {
                try
                {
                    var subject = $"Order Confirmation - Order #{dbOrder.OrderID}";
                    var htmlBody = BuildOrderEmailHtml(dbOrder, vm.CartItems);
                    await _emailService.SendEmailAsync(dbOrder.Email, subject, htmlBody);
                }
                catch (Exception ex)
                {
                    // Log the failure but never break the order flow.
                    _logger.LogError(ex,
                        "Failed to send order confirmation email for order {OrderId}", dbOrder.OrderID);
                }
            }

            var confirmVm = MapDbOrderToVm(dbOrder, vm.CartItems);

            TempData["OrderConfirmation"] = JsonSerializer.Serialize(confirmVm);
            return RedirectToAction("OrderConfirmation");
        }

        // GET: /Home/OrderConfirmation
        public IActionResult OrderConfirmation()
        {
            if (TempData["OrderConfirmation"] is not string json)
                return RedirectToAction("Cart");

            var vm = JsonSerializer.Deserialize<OrderConfirmationViewModel>(json);
            if (vm == null) return RedirectToAction("Cart");

            TempData.Keep("OrderConfirmation");
            return View(vm);
        }

        // GET: /Home/MyOrders
        public async Task<IActionResult> MyOrders()
        {
            var dbOrders = await _db.Orders
                .OrderByDescending(o => o.OrderDate)
                .ToListAsync();

            var orders = dbOrders.Select(o => MapDbOrderToVm(o, new List<CheckoutItem>())).ToList();
            return View(orders);
        }

        // GET: /Home/OrderDetail?id=ORD-5
        public async Task<IActionResult> OrderDetail(string? id)
        {
            if (!string.IsNullOrEmpty(id) && id.StartsWith("ORD-") &&
                int.TryParse(id["ORD-".Length..], out int numericId))
            {
                var dbOrder = await _db.Orders.FindAsync(numericId);
                if (dbOrder != null)
                {
                    var dbItems = await _db.OrderItems
                        .Where(i => i.OrderID == numericId)
                        .ToListAsync();

                    var productIds = dbItems.Select(i => i.ProductId).Distinct().ToList();
                    var products = await _db.Products
                        .Where(p => productIds.Contains(p.ProductId))
                        .ToListAsync();

                    var checkoutItems = dbItems.Select(i =>
                    {
                        var p = products.FirstOrDefault(pr => pr.ProductId == i.ProductId);
                        return new CheckoutItem
                        {
                            ProductId = i.ProductId,
                            Name = p?.ProductName ?? "Unknown",
                            Image = p?.ImagePath ?? "",
                            Size = i.Size ?? "",
                            Price = i.UnitPrice,
                            Quantity = i.Quantity
                        };
                    }).ToList();

                    return View(MapDbOrderToVm(dbOrder, checkoutItems));
                }
            }

            // Fallback: last confirmed order in TempData
            if (TempData["OrderConfirmation"] is string json)
            {
                var vm = JsonSerializer.Deserialize<OrderConfirmationViewModel>(json);
                if (vm != null) return View(vm);
            }

            return RedirectToAction("MyOrders");
        }

        // POST: /Home/CancelOrder
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CancelOrder(string orderId, string cancellationReason)
        {
            if (!string.IsNullOrEmpty(orderId) && orderId.StartsWith("ORD-") &&
                int.TryParse(orderId["ORD-".Length..], out int numericId))
            {
                var dbOrder = await _db.Orders.FindAsync(numericId);
                if (dbOrder != null)
                {
                    if (dbOrder.Status != "Placed")
                    {
                        TempData["ErrorMessage"] = "This order can no longer be cancelled because it has already been processed.";
                        return RedirectToAction("OrderDetail", new { id = orderId });
                    }

                    dbOrder.Status = "Cancelled";
                    dbOrder.CancellationReason = cancellationReason?.Trim();
                    await _db.SaveChangesAsync();

                    TempData["SuccessMessage"] = "Your order has been successfully cancelled.";
                    return RedirectToAction("OrderDetail", new { id = orderId });
                }
            }

            TempData["ErrorMessage"] = "Order not found.";
            return RedirectToAction("MyOrders");
        }

        // GET: /Home/Wishlist
        public IActionResult Wishlist() => View();

        // GET: /Home/SellerShop?id=1
        public IActionResult SellerShop(int id)
        {
            ViewData["SellerId"] = id;
            return View();
        }

        // GET: /Home/Error
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error() => View();

        // ── Private helpers ──────────────────────────────────────────────────

        private async Task<List<CheckoutItem>> GetCheckoutItemsAsync()
        {
            var productIds = ProductData.Cart.Select(c => c.ProductId).Distinct().ToList();

            var dbProducts = await _db.Products
                .Where(p => productIds.Contains(p.ProductId))
                .ToListAsync();

            return ProductData.Cart
                .Select(ci =>
                {
                    var p = dbProducts.FirstOrDefault(d => d.ProductId == ci.ProductId);
                    if (p == null) return null;

                    decimal price = p.Discount.HasValue && p.Discount.Value > 0
                        ? p.Price - p.Discount.Value
                        : p.Price;

                    return new CheckoutItem
                    {
                        ProductId = ci.ProductId,
                        Name = p.ProductName,
                        Image = p.ImagePath ?? "",
                        Size = ci.Size,
                        Price = price,
                        Quantity = ci.Quantity
                    };
                })
                .Where(i => i != null)
                .Select(i => i!)
                .ToList();
        }

        private async Task<CheckoutViewModel> BuildCheckoutVmAsync()
        {
            var items = await GetCheckoutItemsAsync();
            return new CheckoutViewModel
            {
                CartItems = items,
                Subtotal = items.Sum(i => i.Price * i.Quantity),
                ShippingFee = 150m,
            };
        }

        private static OrderConfirmationViewModel MapDbOrderToVm(DbOrder o, List<CheckoutItem> items)
        {
            return new OrderConfirmationViewModel
            {
                OrderId = $"ORD-{o.OrderID}",
                OrderStatus = o.Status,
                FullName = o.FullName,
                Email = o.Email,
                Phone = o.PhoneNumber,
                Address = o.StreetAddress,
                City = o.City,
                PostalCode = o.PostalCode,
                DeliveryOption = o.DeliveryOption,
                PaymentMethod = o.PaymentMethod,
                CreatedAt = o.OrderDate,
                EstimatedDeliveryDate = o.EstimatedDeliveryDate,
                Subtotal = o.SubTotal,
                ShippingFee = o.ShippingFee,
                CancellationReason = o.CancellationReason,
                OrderItems = items
            };
        }
        // Builds an HTML email body for the order confirmation sent to the consumer.
        private static string BuildOrderEmailHtml(DbOrder order, List<CheckoutItem> items)
        {
            var rows = string.Join("", items.Select(i =>
                $"""
        <tr>
          <td style="padding:12px 16px;border-bottom:1px solid #f0f0f0">{i.Name}</td>
          <td style="padding:12px 16px;border-bottom:1px solid #f0f0f0;text-align:center">{i.Size}</td>
          <td style="padding:12px 16px;border-bottom:1px solid #f0f0f0;text-align:center">{i.Quantity}</td>
          <td style="padding:12px 16px;border-bottom:1px solid #f0f0f0;text-align:right">&#8369;{i.Price:N2}</td>
          <td style="padding:12px 16px;border-bottom:1px solid #f0f0f0;text-align:right">&#8369;{i.Price * i.Quantity:N2}</td>
        </tr>
        """));

            return $"""
        <!DOCTYPE html>
        <html>
        <body style="font-family:Arial,sans-serif;color:#333;background:#f4f4f4;margin:0;padding:0">
          <div style="max-width:640px;margin:32px auto;background:#fff;border-radius:8px;overflow:hidden;box-shadow:0 2px 8px rgba(0,0,0,0.08)">

            <!-- Header -->
            <div style="background:#111;padding:28px 32px;text-align:center">
              <h1 style="color:#fff;margin:0;font-size:24px;letter-spacing:2px">NEW HORIZON</h1>
              <p style="color:#aaa;margin:6px 0 0;font-size:13px">Order Confirmation</p>
            </div>

            <!-- Greeting -->
            <div style="padding:32px 32px 0">
              <h2 style="margin:0 0 8px;font-size:20px">Thank you, {order.FullName}! &#127881;</h2>
              <p style="margin:0;color:#666;font-size:14px">Your order has been successfully placed. Here's a summary below.</p>
            </div>

            <!-- Order Info Card -->
            <div style="margin:24px 32px;background:#f9f9f9;border-radius:6px;padding:20px;font-size:14px">
              <table width="100%" style="border-collapse:collapse">
                <tr>
                  <td style="padding:5px 0;color:#888;width:140px">Order ID</td>
                  <td style="padding:5px 0"><strong>ORD-{order.OrderID}</strong></td>
                  <td style="padding:5px 0;color:#888;width:140px">Date</td>
                  <td style="padding:5px 0">{order.OrderDate:MMM d, yyyy h:mm tt}</td>
                </tr>
                <tr>
                  <td style="padding:5px 0;color:#888">Email</td>
                  <td style="padding:5px 0">{order.Email}</td>
                  <td style="padding:5px 0;color:#888">Phone</td>
                  <td style="padding:5px 0">{order.PhoneNumber}</td>
                </tr>
                <tr>
                  <td style="padding:5px 0;color:#888">Address</td>
                  <td style="padding:5px 0" colspan="3">{order.StreetAddress}, {order.City} {order.PostalCode}</td>
                </tr>
                <tr>
                  <td style="padding:5px 0;color:#888">Delivery</td>
                  <td style="padding:5px 0">{order.DeliveryOption}</td>
                  <td style="padding:5px 0;color:#888">Payment</td>
                  <td style="padding:5px 0">{order.PaymentMethod}</td>
                </tr>
                <tr>
                  <td style="padding:5px 0;color:#888">Est. Delivery</td>
                  <td style="padding:5px 0" colspan="3">{order.EstimatedDeliveryDate:MMMM d, yyyy}</td>
                </tr>
              </table>
            </div>

            <!-- Order Items -->
            <div style="padding:0 32px">
              <h3 style="font-size:15px;margin:0 0 12px;color:#111">Order Items</h3>
              <table width="100%" style="border-collapse:collapse;font-size:14px">
                <thead>
                  <tr style="background:#111;color:#fff">
                    <th style="padding:10px 16px;text-align:left;font-weight:600">Product</th>
                    <th style="padding:10px 16px;text-align:center;font-weight:600">Size</th>
                    <th style="padding:10px 16px;text-align:center;font-weight:600">Qty</th>
                    <th style="padding:10px 16px;text-align:right;font-weight:600">Unit Price</th>
                    <th style="padding:10px 16px;text-align:right;font-weight:600">Subtotal</th>
                  </tr>
                </thead>
                <tbody>{rows}</tbody>
              </table>

              <!-- Totals -->
              <table width="100%" style="border-collapse:collapse;font-size:14px;margin-top:0">
                <tr>
                  <td colspan="4" style="padding:10px 16px;text-align:right;color:#666;border-top:1px solid #f0f0f0">Subtotal</td>
                  <td style="padding:10px 16px;text-align:right;color:#666;border-top:1px solid #f0f0f0;min-width:100px">&#8369;{order.SubTotal:N2}</td>
                </tr>
                <tr>
                  <td colspan="4" style="padding:10px 16px;text-align:right;color:#666">Shipping Fee</td>
                  <td style="padding:10px 16px;text-align:right;color:#666">&#8369;{order.ShippingFee:N2}</td>
                </tr>
                <tr style="background:#111;color:#fff">
                  <td colspan="4" style="padding:12px 16px;text-align:right;font-weight:700;font-size:15px;border-radius:0 0 0 6px">Total Amount</td>
                  <td style="padding:12px 16px;text-align:right;font-weight:700;font-size:15px;border-radius:0 0 6px 0">&#8369;{order.TotalAmount:N2}</td>
                </tr>
              </table>
            </div>

            <!-- Footer -->
            <div style="margin:32px;padding:20px;text-align:center;border-top:1px solid #f0f0f0">
              <p style="margin:0;color:#999;font-size:13px">Thank you for shopping with <strong style="color:#111">New Horizon</strong>!</p>
              <p style="margin:6px 0 0;color:#bbb;font-size:12px">If you have questions about your order, please contact us.</p>
            </div>

          </div>
        </body>
        </html>
        """;
        }
    }
}
    