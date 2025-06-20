using Microsoft.AspNetCore.Mvc;
using DatVeXe.Models;
using DatVeXe.Services;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DatVeXe.Controllers
{
    [Route("[controller]")]
    public class CheckoutController : Controller
    {
        private readonly ILogger<CheckoutController> _logger;
        private readonly IMoMoHelper _moMoHelper;
        private readonly IPaymentService _paymentService;
        private readonly DatVeXeContext _context;

        public CheckoutController(ILogger<CheckoutController> logger, IMoMoHelper moMoHelper, IPaymentService paymentService, DatVeXeContext context)
        {
            _logger = logger;
            _moMoHelper = moMoHelper;
            _paymentService = paymentService;
            _context = context;
        }

        [HttpGet("PaymentCallBack")]
        public async Task<IActionResult> PaymentCallBack()
        {
            try
            {
                _logger.LogInformation("MoMo PaymentCallBack received");

                // Lấy parameters từ query string
                var parameters = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
                _logger.LogInformation($"PaymentCallBack parameters: {string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}");

                var orderId = parameters.GetValueOrDefault("orderId");
                var resultCode = parameters.GetValueOrDefault("resultCode");

                _logger.LogInformation($"PaymentCallBack - OrderId: {orderId}, ResultCode: {resultCode}");

                if (string.IsNullOrEmpty(orderId))
                {
                    _logger.LogWarning("Missing orderId in PaymentCallBack");
                    return RedirectToAction("PaymentFailure", "Booking", new {
                        errorMessage = "Thiếu thông tin đơn hàng",
                        errorCode = "MISSING_ORDER_ID"
                    });
                }

                // Extract original orderId from uniqueOrderId format: {orderId}_{ticks}
                var originalOrderId = orderId;
                if (orderId.Contains("_"))
                {
                    originalOrderId = orderId.Split('_')[0];
                    _logger.LogInformation($"Extracted original orderId: {originalOrderId} from uniqueOrderId: {orderId}");
                }

                // Kiểm tra nếu người dùng chọn "Quay về" hoặc hủy thanh toán
                if (resultCode == "1006" || resultCode == "1003")
                {
                    _logger.LogInformation($"User cancelled payment for order: {originalOrderId} (ResultCode: {resultCode})");
                    return RedirectToAction("PaymentFailure", "Booking", new {
                        sessionId = originalOrderId,
                        errorMessage = "Thanh toán bị hủy - Bạn đã chọn quay về hoặc hủy thanh toán",
                        errorCode = "USER_CANCELLED",
                        transactionId = originalOrderId
                    });
                }

                // Xử lý callback thông qua PaymentService
                var paymentResult = await _paymentService.ProcessPaymentCallbackAsync(originalOrderId, parameters);

                if (paymentResult.Success)
                {
                    _logger.LogInformation($"Payment successful for order: {originalOrderId}");

                    // Tìm sessionId từ VeId (originalOrderId)
                    var sessionId = await FindSessionIdFromVeId(originalOrderId);
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _logger.LogWarning($"Cannot find sessionId for VeId: {originalOrderId}");
                        // Fallback: sử dụng originalOrderId làm sessionId
                        sessionId = originalOrderId;
                    }

                    return RedirectToAction("PaymentSuccess", "Booking", new {
                        sessionId = sessionId,
                        transactionId = paymentResult.TransactionId
                    });
                }
                else
                {
                    _logger.LogWarning($"Payment failed for order: {originalOrderId}, Error: {paymentResult.Message}");

                    // Tìm sessionId từ VeId (originalOrderId)
                    var sessionId = await FindSessionIdFromVeId(originalOrderId);
                    if (string.IsNullOrEmpty(sessionId))
                    {
                        _logger.LogWarning($"Cannot find sessionId for VeId: {originalOrderId}");
                        // Fallback: sử dụng originalOrderId làm sessionId
                        sessionId = originalOrderId;
                    }

                    return RedirectToAction("PaymentFailure", "Booking", new {
                        sessionId = sessionId,
                        errorMessage = paymentResult.Message,
                        errorCode = paymentResult.ErrorCode,
                        transactionId = paymentResult.TransactionId
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MoMo PaymentCallBack");
                return RedirectToAction("PaymentFailure", "Booking", new {
                    errorMessage = "Có lỗi hệ thống khi xử lý thanh toán",
                    errorCode = "SYSTEM_ERROR"
                });
            }
        }

        // DEPRECATED: Endpoint này không còn được sử dụng
        // MoMo giờ redirect về PaymentCallBack cho tất cả các trường hợp
        [HttpGet("MoMoReturn")]
        public async Task<IActionResult> MoMoReturn()
        {
            try
            {
                _logger.LogInformation("MoMo Return received - User clicked 'Quay về' button");

                // Lấy parameters từ query string
                var parameters = Request.Query.ToDictionary(x => x.Key, x => x.Value.ToString());
                _logger.LogInformation($"MoMoReturn parameters: {string.Join(", ", parameters.Select(p => $"{p.Key}={p.Value}"))}");

                var orderId = parameters.GetValueOrDefault("orderId");
                var resultCode = parameters.GetValueOrDefault("resultCode");

                _logger.LogInformation($"MoMoReturn - OrderId: {orderId}, ResultCode: {resultCode}");

                if (string.IsNullOrEmpty(orderId))
                {
                    _logger.LogWarning("Missing orderId in MoMoReturn");
                    return RedirectToAction("PaymentFailure", "Booking", new {
                        errorMessage = "Thiếu thông tin đơn hàng",
                        errorCode = "MISSING_ORDER_ID"
                    });
                }

                // Extract original orderId from uniqueOrderId format: {orderId}_{ticks}
                var originalOrderId = orderId;
                if (orderId.Contains("_"))
                {
                    originalOrderId = orderId.Split('_')[0];
                    _logger.LogInformation($"Extracted original orderId: {originalOrderId} from uniqueOrderId: {orderId}");
                }

                // Khi người dùng chọn "Quay về" từ MoMo gateway, coi đó là thanh toán thất bại
                // vì người dùng có thể chưa hoàn tất thanh toán
                _logger.LogInformation($"Processing MoMo return as failed payment for order: {originalOrderId} (User chose to return from MoMo gateway without completing payment)");

                // Tìm sessionId từ VeId (originalOrderId)
                var sessionId = await FindSessionIdFromVeId(originalOrderId);
                if (string.IsNullOrEmpty(sessionId))
                {
                    _logger.LogWarning($"Cannot find sessionId for VeId: {originalOrderId}");
                    // Fallback: sử dụng originalOrderId làm sessionId
                    sessionId = originalOrderId;
                }

                return RedirectToAction("PaymentFailure", "Booking", new {
                    sessionId = sessionId,
                    errorMessage = "Thanh toán bị hủy - Bạn đã chọn quay về từ cổng thanh toán MoMo",
                    errorCode = "USER_CANCELLED",
                    transactionId = originalOrderId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MoMo Return");
                return RedirectToAction("PaymentFailure", "Booking", new {
                    errorMessage = "Có lỗi hệ thống khi xử lý thanh toán",
                    errorCode = "SYSTEM_ERROR"
                });
            }
        }

        [HttpPost("MomoNotify")]
        public async Task<IActionResult> MomoNotify([FromBody] MoMoIpnRequest ipnRequest)
        {
            try
            {
                _logger.LogInformation($"MoMo IPN received: {JsonSerializer.Serialize(ipnRequest)}");

                // Validate signature
                var rawSignature = $"accessKey={Request.Headers["accessKey"]}" +
                                 $"&amount={ipnRequest.amount}" +
                                 $"&extraData={ipnRequest.extraData}" +
                                 $"&message={ipnRequest.message}" +
                                 $"&orderId={ipnRequest.orderId}" +
                                 $"&orderInfo={ipnRequest.orderInfo}" +
                                 $"&orderType={ipnRequest.orderType}" +
                                 $"&partnerCode={ipnRequest.partnerCode}" +
                                 $"&payType={ipnRequest.payType}" +
                                 $"&requestId={ipnRequest.requestId}" +
                                 $"&responseTime={ipnRequest.responseTime}" +
                                 $"&resultCode={ipnRequest.resultCode}" +
                                 $"&transId={ipnRequest.transId}";

                var isValidSignature = _moMoHelper.ValidateSignature(ipnRequest.signature, rawSignature);

                if (!isValidSignature)
                {
                    _logger.LogWarning($"Invalid signature in MoMo IPN for order: {ipnRequest.orderId}");
                    return BadRequest(new MoMoIpnResponse
                    {
                        partnerCode = ipnRequest.partnerCode,
                        requestId = ipnRequest.requestId,
                        orderId = ipnRequest.orderId,
                        resultCode = 97, // Invalid signature
                        message = "Invalid signature",
                        responseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    });
                }

                // Process the payment notification
                var parameters = new Dictionary<string, string>
                {
                    ["partnerCode"] = ipnRequest.partnerCode,
                    ["orderId"] = ipnRequest.orderId,
                    ["requestId"] = ipnRequest.requestId,
                    ["amount"] = ipnRequest.amount.ToString(),
                    ["orderInfo"] = ipnRequest.orderInfo,
                    ["orderType"] = ipnRequest.orderType,
                    ["transId"] = ipnRequest.transId.ToString(),
                    ["resultCode"] = ipnRequest.resultCode.ToString(),
                    ["message"] = ipnRequest.message,
                    ["payType"] = ipnRequest.payType,
                    ["responseTime"] = ipnRequest.responseTime.ToString(),
                    ["extraData"] = ipnRequest.extraData,
                    ["signature"] = ipnRequest.signature
                };

                // Extract original orderId from uniqueOrderId format: {orderId}_{ticks}
                var originalOrderId = ipnRequest.orderId;
                if (ipnRequest.orderId.Contains("_"))
                {
                    originalOrderId = ipnRequest.orderId.Split('_')[0];
                    _logger.LogInformation($"IPN - Extracted original orderId: {originalOrderId} from uniqueOrderId: {ipnRequest.orderId}");
                }

                var paymentResult = await _paymentService.ProcessPaymentCallbackAsync(originalOrderId, parameters);

                // Return response to MoMo
                var response = new MoMoIpnResponse
                {
                    partnerCode = ipnRequest.partnerCode,
                    requestId = ipnRequest.requestId,
                    orderId = ipnRequest.orderId,
                    resultCode = paymentResult.Success ? 0 : 99,
                    message = paymentResult.Success ? "Success" : "Failed",
                    responseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                };

                _logger.LogInformation($"MoMo IPN response: {JsonSerializer.Serialize(response)}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing MoMo IPN");
                return StatusCode(500, new MoMoIpnResponse
                {
                    partnerCode = ipnRequest?.partnerCode ?? "",
                    requestId = ipnRequest?.requestId ?? "",
                    orderId = ipnRequest?.orderId ?? "",
                    resultCode = 99,
                    message = "Internal server error",
                    responseTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
            }
        }

        // Helper method để tìm sessionId từ VeId
        private async Task<string?> FindSessionIdFromVeId(string veIdStr)
        {
            try
            {
                if (!int.TryParse(veIdStr, out int veId))
                {
                    _logger.LogWarning($"Invalid VeId format: {veIdStr}");
                    return null;
                }

                // Tìm vé theo VeId
                var ve = await _context.Ves
                    .Include(v => v.ThanhToans)
                    .FirstOrDefaultAsync(v => v.VeId == veId);

                if (ve == null)
                {
                    _logger.LogWarning($"Ve not found for VeId: {veId}");
                    return null;
                }

                // Tìm trong tất cả các session hiện tại để tìm session có MaVe tương ứng
                var sessionKeys = HttpContext.Session.Keys.Where(k => k.StartsWith("Booking_")).ToList();

                foreach (var sessionKey in sessionKeys)
                {
                    try
                    {
                        var sessionData = HttpContext.Session.GetString(sessionKey);
                        if (!string.IsNullOrEmpty(sessionData))
                        {
                            var bookingStep = JsonSerializer.Deserialize<BookingStepViewModel>(sessionData);
                            if (bookingStep?.MaVe == ve.MaVe)
                            {
                                // Trích xuất sessionId từ key "Booking_{sessionId}"
                                var sessionId = sessionKey.Substring("Booking_".Length);
                                _logger.LogInformation($"Found sessionId: {sessionId} for VeId: {veId}, MaVe: {ve.MaVe}");
                                return sessionId;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, $"Error deserializing session data for key: {sessionKey}");
                        continue;
                    }
                }

                _logger.LogWarning($"No session found for VeId: {veId}, MaVe: {ve.MaVe}");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error finding sessionId for VeId: {veIdStr}");
                return null;
            }
        }
    }
}
