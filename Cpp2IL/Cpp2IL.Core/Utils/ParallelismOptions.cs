using System.Linq;
using System.Threading.Tasks;

namespace Cpp2IL.Core.Utils;

public static class ParallelismOptions
{
    public static int MaxDegreeOfParallelism { get; set; } = -1;

    public static ParallelOptions Create()
        => new() { MaxDegreeOfParallelism = MaxDegreeOfParallelism };

    public static ParallelQuery<T> WithConfiguredParallelism<T>(this ParallelQuery<T> query)
        => MaxDegreeOfParallelism > 0
            ? query.WithDegreeOfParallelism(MaxDegreeOfParallelism)
            : query;
}
