using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

namespace DatVeXe.Attributes
{
    /// <summary>
    /// Validation attribute for Vietnamese phone numbers
    /// </summary>
    public class VietnamesePhoneAttribute : ValidationAttribute
    {
        private static readonly Regex PhoneRegex = new Regex(
            @"^(0|\+84)(3[2-9]|5[689]|7[06-9]|8[1-689]|9[0-46-9])[0-9]{7}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return false;

            var phoneNumber = value.ToString()!.Trim();
            
            // Remove spaces and dashes
            phoneNumber = phoneNumber.Replace(" ", "").Replace("-", "");
            
            return PhoneRegex.IsMatch(phoneNumber);
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} không đúng định dạng số điện thoại Việt Nam";
        }
    }

    /// <summary>
    /// Validation attribute for Vietnamese names
    /// </summary>
    public class VietnameseNameAttribute : ValidationAttribute
    {
        private static readonly Regex NameRegex = new Regex(
            @"^[a-zA-ZÀÁÂÃÈÉÊÌÍÒÓÔÕÙÚĂĐĨŨƠàáâãèéêìíòóôõùúăđĩũơƯĂẠẢẤẦẨẪẬẮẰẲẴẶẸẺẼỀỀỂưăạảấầẩẫậắằẳẵặẹẻẽềềểỄỆỈỊỌỎỐỒỔỖỘỚỜỞỠỢỤỦỨỪễệỉịọỏốồổỗộớờởỡợụủứừỬỮỰỲỴÝỶỸửữựỳỵýỷỹ\s]+$",
            RegexOptions.Compiled);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return false;

            var name = value.ToString()!.Trim();
            
            // Check length
            if (name.Length < 2 || name.Length > 100)
                return false;

            // Check format
            if (!NameRegex.IsMatch(name))
                return false;

            // Check for consecutive spaces
            if (name.Contains("  "))
                return false;

            return true;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} phải từ 2-100 ký tự, chỉ chứa chữ cái và khoảng trắng";
        }
    }

    /// <summary>
    /// Validation attribute for future dates
    /// </summary>
    public class FutureDateAttribute : ValidationAttribute
    {
        private readonly int _minimumHours;

        public FutureDateAttribute(int minimumHours = 0)
        {
            _minimumHours = minimumHours;
        }

        public override bool IsValid(object? value)
        {
            if (value == null)
                return false;

            if (value is DateTime dateTime)
            {
                return dateTime > DateTime.Now.AddHours(_minimumHours);
            }

            return false;
        }

        public override string FormatErrorMessage(string name)
        {
            if (_minimumHours > 0)
                return $"{name} phải sau thời điểm hiện tại ít nhất {_minimumHours} giờ";
            
            return $"{name} phải là thời điểm trong tương lai";
        }
    }

    /// <summary>
    /// Validation attribute for ticket codes
    /// </summary>
    public class TicketCodeAttribute : ValidationAttribute
    {
        private static readonly Regex TicketCodeRegex = new Regex(
            @"^[A-Z0-9]{6,12}$",
            RegexOptions.Compiled);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return false;

            var ticketCode = value.ToString()!.Trim().ToUpper();
            return TicketCodeRegex.IsMatch(ticketCode);
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} phải từ 6-12 ký tự, chỉ chứa chữ cái in hoa và số";
        }
    }

    /// <summary>
    /// Validation attribute for positive decimal values
    /// </summary>
    public class PositiveDecimalAttribute : ValidationAttribute
    {
        private readonly decimal _minimum;

        public PositiveDecimalAttribute(double minimum = 0.01)
        {
            _minimum = (decimal)minimum;
        }

        public override bool IsValid(object? value)
        {
            if (value == null)
                return false;

            if (decimal.TryParse(value.ToString(), out decimal decimalValue))
            {
                return decimalValue >= _minimum;
            }

            return false;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} phải lớn hơn hoặc bằng {_minimum:N0}";
        }
    }

    /// <summary>
    /// Validation attribute for seat numbers
    /// </summary>
    public class SeatNumberAttribute : ValidationAttribute
    {
        private static readonly Regex SeatNumberRegex = new Regex(
            @"^[A-Z]?[0-9]{1,3}[A-Z]?$",
            RegexOptions.Compiled);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return false;

            var seatNumber = value.ToString()!.Trim().ToUpper();
            return SeatNumberRegex.IsMatch(seatNumber);
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} không đúng định dạng (ví dụ: A1, 12, B15)";
        }
    }

    /// <summary>
    /// Validation attribute for promo codes
    /// </summary>
    public class PromoCodeAttribute : ValidationAttribute
    {
        private static readonly Regex PromoCodeRegex = new Regex(
            @"^[A-Z0-9]{3,20}$",
            RegexOptions.Compiled);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return true; // Promo code is optional

            var promoCode = value.ToString()!.Trim().ToUpper();
            return PromoCodeRegex.IsMatch(promoCode);
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} phải từ 3-20 ký tự, chỉ chứa chữ cái in hoa và số";
        }
    }

    /// <summary>
    /// Validation attribute for email addresses with Vietnamese domain support
    /// </summary>
    public class VietnameseEmailAttribute : ValidationAttribute
    {
        private static readonly Regex EmailRegex = new Regex(
            @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public override bool IsValid(object? value)
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
                return true; // Email is optional in some cases

            var email = value.ToString()!.Trim().ToLower();
            
            if (!EmailRegex.IsMatch(email))
                return false;

            // Additional checks
            if (email.Length > 254) // RFC 5321 limit
                return false;

            var parts = email.Split('@');
            if (parts[0].Length > 64) // Local part limit
                return false;

            return true;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} không đúng định dạng email";
        }
    }

    /// <summary>
    /// Validation attribute for date ranges
    /// </summary>
    public class DateRangeAttribute : ValidationAttribute
    {
        private readonly int _maxDaysFromNow;

        public DateRangeAttribute(int maxDaysFromNow = 365)
        {
            _maxDaysFromNow = maxDaysFromNow;
        }

        public override bool IsValid(object? value)
        {
            if (value == null)
                return false;

            if (value is DateTime dateTime)
            {
                var now = DateTime.Now;
                return dateTime >= now && dateTime <= now.AddDays(_maxDaysFromNow);
            }

            return false;
        }

        public override string FormatErrorMessage(string name)
        {
            return $"{name} phải trong khoảng từ hôm nay đến {_maxDaysFromNow} ngày tới";
        }
    }
}
