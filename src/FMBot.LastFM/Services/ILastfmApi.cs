using System.Collections.Generic;
using System.Threading.Tasks;
using FMBot.Domain.ApiModels;

namespace FMBot.LastFM.Services
{
    public interface ILastfmApi
    {
        Task<Response<T>> CallApiAsync<T>(Dictionary<string, string> parameters, string call);
    }
}
