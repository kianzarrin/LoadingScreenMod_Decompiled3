using System;
using System.Collections.Generic;
using System.Linq;

namespace LoadingScreenMod
{
	internal static class Exts
	{
		internal static IEnumerable<Item> Which(this IEnumerable<Item> items, CustomAssetMetaData.Type type, int usage = 0)
		{
			return items.Where((usage != 0) ? ((Func<Item, bool>)((Item item) => item.type == type && (item.usage & usage) != 0)) : ((Func<Item, bool>)((Item item) => item.type == type)));
		}

		internal static IEnumerable<Item> Which(this IEnumerable<Item> items, int usage)
		{
			return items.Where((Item item) => (item.usage & usage) != 0);
		}
	}
}
