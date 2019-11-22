using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CosmosStressTester
{
    [Serializable]
    public class PerfTestDto
    {
        public string PartitionKey => Guid.NewGuid().ToString();
        public int StoreNumber { get; set; }
        public string Key { get; set; }
        public string Value { get; set; }
    }
}
