using System.Threading.Tasks;
using FMBot.Persistence.Domain.Models;

namespace FMBot.Bot.Interfaces
{
    public interface IUpdateService
    {
        Task UpdateUser(User user);
    }
}
