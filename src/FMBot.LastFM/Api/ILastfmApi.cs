using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Domain.Types;
using FMBot.LastFM.Domain.Types;

namespace FMBot.LastFM.Api;

public interface ILastfmApi
{
    Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call, bool generateSignature = false, bool usePrivateKey = false);
}
