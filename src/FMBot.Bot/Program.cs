using System.Threading.Tasks;

namespace FMBot.Bot;

class Program
{
    public static Task Main(string[] args)
        => Startup.RunAsync(args);
}
