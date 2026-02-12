using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public class BondItem
    {
        public decimal Nominal { get; set; }
        public decimal Coupon { get; set; }
        public int CouponsPerYear { get; set; }
        public DateTime NextCouponDate { get; set; }
        public decimal CurrentYield { get; set; }
    }

}
