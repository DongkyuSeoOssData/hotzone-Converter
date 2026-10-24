using System.Reflection;

namespace Hotzone.Core;

/// <summary>간선 그래프에서 최대 2단계 최소 비용 경로를 찾는다 (설계 4.4 ③④).</summary>
public sealed class ConverterRegistry
{
    public const int MaxSteps = 2;

    private readonly Dictionary<(string From, string To), Edge> _edges = new();
    private readonly Func<string, bool> _requirementMet;

    /// <param name="requirementMet">"Office", "LibreOffice" 같은 Requires 이름이 충족되는지.</param>
    public ConverterRegistry(Func<string, bool>? requirementMet = null)
    {
        _requirementMet = requirementMet ?? (_ => true);
    }

    public ConverterRegistry Register(IConverter converter)
    {
        foreach (var attr in converter.GetType().GetCustomAttributes<ConvertsAttribute>())
        {
            if (attr.Requires is { } req && !_requirementMet(req)) continue;
            var (from, to) = attr.Parse();
            foreach (var f in from)
            foreach (var t in to)
            {
                if (f == t) continue;
                // 같은 간선에 변환기가 여럿이면 Cost가 낮은 쪽 우선 (Office COM > LibreOffice)
                if (!_edges.TryGetValue((f, t), out var existing) || attr.Cost < existing.Cost)
                    _edges[(f, t)] = new Edge(f, t, attr.Cost, converter, attr.Chainable);
            }
        }
        return this;
    }

    private IEnumerable<Edge> Outgoing(string from) => _edges.Values.Where(e => e.From == from);

    public IReadOnlyList<Edge>? FindPath(string from, string to)
    {
        if (from == to) return null;
        IReadOnlyList<Edge>? best = null;
        var bestCost = int.MaxValue;
        if (_edges.TryGetValue((from, to), out var direct))
        {
            best = [direct];
            bestCost = direct.Cost;
        }
        foreach (var first in Outgoing(from))
        {
            if (first.To == to || !first.Chainable) continue;
            if (_edges.TryGetValue((first.To, to), out var second) && first.Cost + second.Cost < bestCost)
            {
                best = [first, second];
                bestCost = first.Cost + second.Cost;
            }
        }
        return best;
    }

    public IReadOnlySet<string> TargetsFor(string from)
    {
        var result = new HashSet<string>();
        foreach (var first in Outgoing(from))
        {
            result.Add(first.To);
            if (!first.Chainable) continue;
            foreach (var second in Outgoing(first.To)) result.Add(second.To);
        }
        result.Remove(from);
        return result;
    }

    /// <summary>여러 파일을 끌었을 때 1:1 변환 타일 = 출력 가능 형식의 교집합. 자기 자신으로의 변환은 숨긴다.</summary>
    public IReadOnlySet<string> CommonTargets(IReadOnlyCollection<string> sources)
    {
        if (sources.Count == 0) return new HashSet<string>();
        var result = new HashSet<string>(TargetsFor(sources.First()));
        foreach (var s in sources.Skip(1)) result.IntersectWith(TargetsFor(s));
        var distinct = sources.Distinct().ToList();
        if (distinct.Count == 1) result.Remove(distinct[0]);
        return result;
    }
}
