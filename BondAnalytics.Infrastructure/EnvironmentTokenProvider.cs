using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Domain;

namespace Infrastructure
{
    public class EnvironmentTokenProvider : ITokenProvider
    {
        private const string VarName = "BOND_ANALYTICS_TOKEN";

        public Task<string?> GetTokenAsync()
        {
            var value = Environment.GetEnvironmentVariable(VarName, EnvironmentVariableTarget.User);
            return Task.FromResult(value);
        }

        public Task SaveTokenAsync(string token)
        {
            Environment.SetEnvironmentVariable(VarName, token, EnvironmentVariableTarget.User);
            return Task.CompletedTask;
        }
    }
}

