using SimuladorIndustrial.Core.Tags;

namespace SimuladorIndustrial.Core.Runtime;

public interface ISimulationSystem
{
    void Step(SimulationTick tick, TagRegistry tags);

    void Reset(TagRegistry tags);
}
