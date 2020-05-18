using System.Collections.Generic;

namespace WhoWas
{
    public class Character
    {
        public ulong LodestoneId { get; set; }

        public IDictionary<string, string> NameWorlds { get; set; }

        public Character()
        {
            NameWorlds = new Dictionary<string, string>();
        }
    }
}
