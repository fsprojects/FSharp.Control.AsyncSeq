using System.Threading.Channels;
namespace Helper
{
    public static class CreateChannel
    {
        public static Channel<T> CreateUnbounded<T>()
        {
            return Channel.CreateUnbounded<T>(new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });
        }
        public static Channel<T> CreateBounded<T>(int Capacity)
        {
            return Channel.CreateBounded<T>(new BoundedChannelOptions(Capacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });
        }
    }
}
