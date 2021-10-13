using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using ColossalFramework;
using UnityEngine;

namespace LoadingScreenMod
{
	public sealed class PrefabLoader : DetourUtility<PrefabLoader>
	{
		private delegate void UpdatePrefabs(Array prefabs);

		private delegate void UpdateCollection(string name, Array keptPrefabs, string[] replacedNames);

		private readonly FieldInfo hasQueuedActionsField = typeof(LoadingManager).GetField("m_hasQueuedActions", BindingFlags.Instance | BindingFlags.NonPublic);

		private readonly FieldInfo[] nameField = new FieldInfo[3];

		private readonly FieldInfo[] prefabsField = new FieldInfo[3];

		private readonly FieldInfo[] replacesField = new FieldInfo[3];

		private readonly FieldInfo netPrefabsField;

		private readonly HashSet<string>[] skippedPrefabs = new HashSet<string>[3];

		private HashSet<string> simulationPrefabs;

		private HashSet<string> keptProps = new HashSet<string>();

		private Matcher skipMatcher = Settings.settings.SkipMatcher;

		private Matcher exceptMatcher = Settings.settings.ExceptMatcher;

		private bool saveDeserialized;

		private const string ROUTINE = "<InitializePrefabs>c__Iterator0";

		internal HashSet<string> SkippedProps => skippedPrefabs[2];

		private PrefabLoader()
		{
			try
			{
				int num = 0;
				Type[] array = new Type[3]
				{
					typeof(BuildingCollection),
					typeof(VehicleCollection),
					typeof(PropCollection)
				};
				for (int i = 0; i < array.Length; i++)
				{
					Type nestedType = array[i].GetNestedType("<InitializePrefabs>c__Iterator0", BindingFlags.NonPublic);
					nameField[num] = nestedType.GetField("name", BindingFlags.Instance | BindingFlags.NonPublic);
					prefabsField[num] = nestedType.GetField("prefabs", BindingFlags.Instance | BindingFlags.NonPublic);
					replacesField[num] = nestedType.GetField("replaces", BindingFlags.Instance | BindingFlags.NonPublic);
					skippedPrefabs[num++] = new HashSet<string>();
				}
				netPrefabsField = typeof(NetCollection).GetNestedType("<InitializePrefabs>c__Iterator0", BindingFlags.NonPublic).GetField("prefabs", BindingFlags.Instance | BindingFlags.NonPublic);
				init(typeof(LoadingManager), "QueueLoadingAction");
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		internal void SetSkippedPrefabs(HashSet<string>[] prefabs)
		{
			prefabs.CopyTo(skippedPrefabs, 0);
		}

		internal override void Dispose()
		{
			base.Dispose();
			skipMatcher = (exceptMatcher = null);
			simulationPrefabs?.Clear();
			simulationPrefabs = null;
			Instance<LevelLoader>.instance.SetSkippedPrefabs(skippedPrefabs);
			Array.Clear(skippedPrefabs, 0, skippedPrefabs.Length);
		}

		public static void QueueLoadingAction(LoadingManager lm, IEnumerator action)
		{
			Type declaringType = action.GetType().DeclaringType;
			int exceptions = -1;
			if (declaringType == typeof(BuildingCollection))
			{
				exceptions = 0;
			}
			else if (declaringType == typeof(VehicleCollection))
			{
				exceptions = 1;
			}
			else if (declaringType == typeof(PropCollection))
			{
				exceptions = 2;
			}
			if (exceptions >= 0 && !Instance<PrefabLoader>.instance.saveDeserialized)
			{
				while (!Instance<LevelLoader>.instance.IsSaveDeserialized())
				{
					Thread.Sleep(60);
				}
				Instance<PrefabLoader>.instance.saveDeserialized = true;
			}
			while (!Monitor.TryEnter(Instance<LevelLoader>.instance.loadingLock, SimulationManager.SYNCHRONIZE_TIMEOUT))
			{
			}
			try
			{
				switch (exceptions)
				{
				case 0:
					Instance<PrefabLoader>.instance.Skip<BuildingInfo>(action, UpdateBuildingPrefabs, UpdateBuildingCollection, exceptions);
					break;
				case 1:
					Instance<PrefabLoader>.instance.Skip<VehicleInfo>(action, UpdateVehiclePrefabs, UpdateVehicleCollection, exceptions);
					break;
				case 2:
					Instance<PrefabLoader>.instance.Skip<PropInfo>(action, UpdatePropPrefabs, UpdatePropCollection, exceptions);
					break;
				default:
					if (Instance<PrefabLoader>.instance.skipMatcher.Has[2] && declaringType == typeof(NetCollection))
					{
						Instance<PrefabLoader>.instance.RemoveSkippedFromNets(action);
					}
					break;
				}
				Instance<LevelLoader>.instance.mainThreadQueue.Enqueue(action);
				if (Instance<LevelLoader>.instance.mainThreadQueue.Count < 2)
				{
					Instance<PrefabLoader>.instance.hasQueuedActionsField.SetValue(lm, true);
				}
			}
			finally
			{
				Monitor.Exit(Instance<LevelLoader>.instance.loadingLock);
			}
		}

		private void Skip<P>(IEnumerator action, UpdatePrefabs UpdateAll, UpdateCollection UpdateKept, int index) where P : PrefabInfo
		{
			try
			{
				P[] ret = prefabsField[index].GetValue(action) as P[];
				if (ret == null)
				{
					prefabsField[index].SetValue(action, new P[0]);
					return;
				}
				UpdateAll(ret);
				if (!skipMatcher.Has[index])
				{
					return;
				}
				if (index == 0)
				{
					LookupSimulationPrefabs();
				}
				string[] array = replacesField[index].GetValue(action) as string[];
				if (array == null)
				{
					array = new string[0];
				}
				List<P> list = null;
				List<string> list2 = null;
				for (int i = 0; i < ret.Length; i++)
				{
					P val = ret[i];
					string text = ((i >= array.Length) ? string.Empty : array[i]?.Trim());
					if (Skip(val, text, index))
					{
						AddToSkipped(val, text, index);
						Instance<LevelLoader>.instance.skipCounts[index]++;
						if (list == null)
						{
							list = ret.ToList(i);
							if (i < array.Length)
							{
								list2 = array.ToList(i);
							}
						}
					}
					else if (list != null)
					{
						list.Add(val);
						list2?.Add(text);
					}
				}
				if (list != null)
				{
					P[] array2 = list.ToArray();
					string[] array3 = null;
					prefabsField[index].SetValue(action, array2);
					if (list2 != null)
					{
						array3 = list2.ToArray();
						replacesField[index].SetValue(action, array3);
					}
					UpdateKept(nameField[index].GetValue(action) as string, array2, array3);
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void AddToSkipped(PrefabInfo info, string replace, int index)
		{
			HashSet<string> events = skippedPrefabs[index];
			events.Add(info.name);
			if (string.IsNullOrEmpty(replace))
			{
				return;
			}
			if (replace.IndexOf(',') != -1)
			{
				string[] array = replace.Split(',');
				for (int i = 0; i < array.Length; i++)
				{
					events.Add(array[i].Trim());
				}
			}
			else
			{
				events.Add(replace);
			}
		}

		private static void UpdateBuildingPrefabs(Array prefabs)
		{
			if (!Instance<PrefabLoader>.instance.skipMatcher.Has[2])
			{
				return;
			}
			BuildingInfo[] array = prefabs as BuildingInfo[];
			if (array != null)
			{
				for (int e = 0; e < array.Length; e++)
				{
					Instance<PrefabLoader>.instance.RemoveSkippedFromBuilding(array[e]);
				}
			}
		}

		private static void UpdateVehiclePrefabs(Array prefabs)
		{
			if (!Instance<PrefabLoader>.instance.skipMatcher.Has[1])
			{
				return;
			}
			VehicleInfo[] array = prefabs as VehicleInfo[];
			if (array != null)
			{
				for (int e = 0; e < array.Length; e++)
				{
					Instance<PrefabLoader>.instance.RemoveSkippedFromVehicle(array[e]);
				}
			}
		}

		private static void UpdatePropPrefabs(Array prefabs)
		{
			if (!Instance<PrefabLoader>.instance.skipMatcher.Has[2])
			{
				return;
			}
			PropInfo[] array = prefabs as PropInfo[];
			if (array != null)
			{
				for (int i = 0; i < array.Length; i++)
				{
					Instance<PrefabLoader>.instance.RemoveSkippedFromProp(array[i]);
				}
			}
		}

		private static void UpdateBuildingCollection(string name, Array keptPrefabs, string[] replacedNames)
		{
			BuildingCollection i = GameObject.Find(name)?.GetComponent<BuildingCollection>();
			if (i != null)
			{
				i.m_prefabs = keptPrefabs as BuildingInfo[];
				if (replacedNames != null)
				{
					i.m_replacedNames = replacedNames;
				}
			}
		}

		private static void UpdateVehicleCollection(string name, Array keptPrefabs, string[] replacedNames)
		{
			VehicleCollection vehicleCollection = GameObject.Find(name)?.GetComponent<VehicleCollection>();
			if (vehicleCollection != null)
			{
				vehicleCollection.m_prefabs = keptPrefabs as VehicleInfo[];
				if (replacedNames != null)
				{
					vehicleCollection.m_replacedNames = replacedNames;
				}
			}
		}

		private static void UpdatePropCollection(string name, Array keptPrefabs, string[] replacedNames)
		{
			PropCollection propCollection = GameObject.Find(name)?.GetComponent<PropCollection>();
			if (propCollection != null)
			{
				propCollection.m_prefabs = keptPrefabs as PropInfo[];
				if (replacedNames != null)
				{
					propCollection.m_replacedNames = replacedNames;
				}
			}
		}

		private bool Skip(PrefabInfo info, string replace, int index)
		{
			if (skipMatcher.Matches(info, index))
			{
				string name = info.name;
				if (index == 0 && IsSimulationPrefab(name, replace))
				{
					Util.DebugPrint(name + " -> not skipped because used in city");
					return false;
				}
				if (exceptMatcher.Matches(info, index))
				{
					Util.DebugPrint(name + " -> not skipped because excepted");
					return false;
				}
				Util.DebugPrint(name + " -> skipped");
				return true;
			}
			return false;
		}

		private bool Skip(PrefabInfo info, int index)
		{
			if (skipMatcher.Matches(info, index))
			{
				return !exceptMatcher.Matches(info, index);
			}
			return false;
		}

		private bool Skip(PropInfo info)
		{
			string name = info.name;
			if (keptProps.Contains(name))
			{
				return false;
			}
			if (skippedPrefabs[2].Contains(name))
			{
				return true;
			}
			bool num = Skip(info, 2);
			(num ? skippedPrefabs[2] : keptProps).Add(name);
			return num;
		}

		internal void LookupSimulationPrefabs()
		{
			if (simulationPrefabs != null)
			{
				return;
			}
			simulationPrefabs = new HashSet<string>();
			try
			{
				Building[] buffer = Singleton<BuildingManager>.instance.m_buildings.m_buffer;
				int num = buffer.Length;
				for (int i = 1; i < num; i++)
				{
					if (buffer[i].m_flags != 0)
					{
						string t = PrefabCollection<BuildingInfo>.PrefabName(buffer[i].m_infoIndex);
						if (!string.IsNullOrEmpty(t) && t.IndexOf('.') < 0)
						{
							simulationPrefabs.Add(t);
						}
					}
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		internal bool AllPrefabsAvailable()
		{
			return CustomDeserializer.AllAvailable<BuildingInfo>(simulationPrefabs, new HashSet<string>());
		}

		private bool IsSimulationPrefab(string name, string replace)
		{
			if (simulationPrefabs.Contains(name))
			{
				return true;
			}
			if (string.IsNullOrEmpty(replace))
			{
				return false;
			}
			if (replace.IndexOf(',') != -1)
			{
				string[] array = replace.Split(',');
				for (int i = 0; i < array.Length; i++)
				{
					if (simulationPrefabs.Contains(array[i].Trim()))
					{
						return true;
					}
				}
				return false;
			}
			return simulationPrefabs.Contains(replace);
		}

		private void RemoveSkippedFromBuilding(BuildingInfo info)
		{
			BuildingInfo.Prop[] list = info.m_props;
			if (list == null || list.Length == 0)
			{
				return;
			}
			try
			{
				List<BuildingInfo.Prop> list2 = new List<BuildingInfo.Prop>(list.Length);
				bool e = false;
				foreach (BuildingInfo.Prop prop in list)
				{
					if (prop != null)
					{
						if (prop.m_prop == null)
						{
							list2.Add(prop);
						}
						else if (Skip(prop.m_prop))
						{
							prop.m_prop = (prop.m_finalProp = null);
							e = true;
						}
						else
						{
							list2.Add(prop);
						}
					}
				}
				if (e)
				{
					info.m_props = list2.ToArray();
					if (info.m_props.Length == 0)
					{
						CommonBuildingAI commonBuildingAI = info.m_buildingAI as CommonBuildingAI;
						if ((object)commonBuildingAI != null)
						{
							commonBuildingAI.m_ignoreNoPropsWarning = true;
						}
						else
						{
							CommonBuildingAI commonBuildingAI2 = info.GetComponent<BuildingAI>() as CommonBuildingAI;
							if ((object)commonBuildingAI2 != null)
							{
								commonBuildingAI2.m_ignoreNoPropsWarning = true;
							}
						}
					}
				}
				list2.Clear();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void RemoveSkippedFromVehicle(VehicleInfo info)
		{
			VehicleInfo.VehicleTrailer[] uIDynamicFont = info.m_trailers;
			if (uIDynamicFont == null || uIDynamicFont.Length == 0)
			{
				return;
			}
			try
			{
				List<VehicleInfo.VehicleTrailer> list = new List<VehicleInfo.VehicleTrailer>(uIDynamicFont.Length);
				string text = string.Empty;
				bool flag = false;
				bool flag2 = false;
				for (int i = 0; i < uIDynamicFont.Length; i++)
				{
					VehicleInfo info2 = uIDynamicFont[i].m_info;
					if (!(info2 == null))
					{
						string name = info2.name;
						if (text != name)
						{
							flag2 = Skip(info2, 1);
							text = name;
						}
						if (flag2)
						{
							uIDynamicFont[i].m_info = null;
							flag = true;
						}
						else
						{
							list.Add(uIDynamicFont[i]);
						}
					}
				}
				if (flag)
				{
					info.m_trailers = ((list.Count > 0) ? list.ToArray() : null);
				}
				list.Clear();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void RemoveSkippedFromProp(PropInfo info)
		{
			PropInfo.Variation[] variations = info.m_variations;
			if (variations == null || variations.Length == 0)
			{
				return;
			}
			try
			{
				List<PropInfo.Variation> list = new List<PropInfo.Variation>(variations.Length);
				bool flag = false;
				for (int text = 0; text < variations.Length; text++)
				{
					PropInfo prop = variations[text].m_prop;
					if (!(prop == null))
					{
						if (Skip(prop))
						{
							variations[text].m_prop = (variations[text].m_finalProp = null);
							flag = true;
						}
						else
						{
							list.Add(variations[text]);
						}
					}
				}
				if (flag)
				{
					info.m_variations = ((list.Count > 0) ? list.ToArray() : null);
				}
				list.Clear();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void RemoveSkippedFromNets(IEnumerator action)
		{
			try
			{
				NetInfo[] array = netPrefabsField.GetValue(action) as NetInfo[];
				if (array == null)
				{
					netPrefabsField.SetValue(action, new NetInfo[0]);
					return;
				}
				List<NetLaneProps.Prop> list = new List<NetLaneProps.Prop>(16);
				NetInfo[] array2 = array;
				foreach (NetInfo netInfo in array2)
				{
					if (netInfo.m_lanes == null)
					{
						continue;
					}
					for (int j = 0; j < netInfo.m_lanes.Length; j++)
					{
						NetLaneProps laneProps = netInfo.m_lanes[j].m_laneProps;
						if (laneProps == null || laneProps.m_props == null)
						{
							continue;
						}
						bool flag = false;
						for (int k = 0; k < laneProps.m_props.Length; k++)
						{
							NetLaneProps.Prop prop = laneProps.m_props[k];
							if (prop != null)
							{
								if (prop.m_prop == null)
								{
									list.Add(prop);
								}
								else if (Skip(prop.m_prop))
								{
									prop.m_prop = (prop.m_finalProp = null);
									flag = true;
								}
								else
								{
									list.Add(prop);
								}
							}
						}
						if (flag)
						{
							laneProps.m_props = list.ToArray();
						}
						list.Clear();
					}
				}
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		internal static IEnumerator RemoveSkippedFromSimulation()
		{
			if (Instance<PrefabLoader>.instance != null)
			{
				Instance<PrefabLoader>.instance.RemoveSkippedFromSimulation<BuildingInfo>(0);
				yield return null;
				Instance<PrefabLoader>.instance.RemoveSkippedFromSimulation<VehicleInfo>(1);
				yield return null;
				Instance<PrefabLoader>.instance.RemoveSkippedFromSimulation<PropInfo>(2);
				yield return null;
			}
		}

		private void RemoveSkippedFromSimulation<P>(int index) where P : PrefabInfo
		{
			HashSet<string> hashSet = skippedPrefabs[index];
			if (hashSet == null || hashSet.Count == 0)
			{
				return;
			}
			object loader = Util.GetStatic(typeof(PrefabCollection<P>), "m_prefabLock");
			while (!Monitor.TryEnter(loader, SimulationManager.SYNCHRONIZE_TIMEOUT))
			{
			}
			try
			{
				FastList<PrefabCollection<P>.PrefabData> obj = (FastList<PrefabCollection<P>.PrefabData>)Util.GetStatic(typeof(PrefabCollection<P>), "m_simulationPrefabs");
				int size = obj.m_size;
				PrefabCollection<P>.PrefabData[] buffer = obj.m_buffer;
				for (int i = 0; i < size; i++)
				{
					if (buffer[i].m_name != null && hashSet.Contains(buffer[i].m_name))
					{
						buffer[i].m_name = "lsm___" + (i + (index << 12));
						buffer[i].m_refcount = 0;
					}
				}
			}
			finally
			{
				Monitor.Exit(loader);
			}
		}

		internal static void RemoveSkippedFromStyle(DistrictStyle style)
		{
			PrefabLoader prefabLoader = Instance<PrefabLoader>.instance;
			HashSet<string> targetProgress = ((prefabLoader != null) ? prefabLoader.skippedPrefabs[0] : null);
			if (targetProgress == null || targetProgress.Count == 0)
			{
				return;
			}
			try
			{
				BuildingInfo[] buildingInfos = style.GetBuildingInfos();
				((HashSet<BuildingInfo>)Util.Get(style, "m_Infos")).Clear();
				((HashSet<int>)Util.Get(style, "m_AffectedServices")).Clear();
				BuildingInfo[] array = buildingInfos;
				foreach (BuildingInfo buildingInfo in array)
				{
					if (buildingInfo != null && !targetProgress.Contains(buildingInfo.name))
					{
						style.Add(buildingInfo);
					}
				}
				Array.Clear(buildingInfos, 0, buildingInfos.Length);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		internal static void UnloadSkipped()
		{
			if (Instance<PrefabLoader>.instance != null)
			{
				Instance<PrefabLoader>.instance.keptProps.Clear();
				Instance<PrefabLoader>.instance.keptProps = null;
				Instance<PrefabLoader>.instance.simulationPrefabs?.Clear();
				Instance<PrefabLoader>.instance.simulationPrefabs = null;
				int[] inst = Instance<LevelLoader>.instance.skipCounts;
				if (inst[0] > 0)
				{
					Util.DebugPrint("Skipped", inst[0], "building prefabs");
				}
				if (inst[1] > 0)
				{
					Util.DebugPrint("Skipped", inst[1], "vehicle prefabs");
				}
				if (inst[2] > 0)
				{
					Util.DebugPrint("Skipped", inst[2], "prop prefabs");
				}
				try
				{
					Resources.UnloadUnusedAssets();
				}
				catch (Exception exception)
				{
					Debug.LogException(exception);
				}
			}
		}
	}
}
