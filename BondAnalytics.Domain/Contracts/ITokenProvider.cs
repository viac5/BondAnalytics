using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Domain
{
    public interface ITokenProvider
    {
        Task<string?> GetTokenAsync();
        Task SaveTokenAsync(string token);
    }
}

