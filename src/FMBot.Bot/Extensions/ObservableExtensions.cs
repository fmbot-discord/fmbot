using System;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace FMBot.Bot.Extensions
{
    public static class ObservableExtensions
    {
        public static void SubscribeAsync<T>(this IObservable<T> observable, Func<T, Task> subscription)
        {
            observable
                .ObserveOn(NewThreadScheduler.Default)
                .Subscribe(async value =>
                {
                    try
                    {
                        await subscription(value);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Error occured in subscription OnNext");
                    }
                }, ex =>
                {
                    Console.WriteLine("Error occured in subscription OnNext");
                });
        }
    }
}
