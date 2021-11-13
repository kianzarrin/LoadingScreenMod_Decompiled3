using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using ColossalFramework;
using ColossalFramework.Packaging;
using ColossalFramework.PlatformServices;
using ICities;
using UnityEngine;

namespace LoadingScreenMod
{
	public sealed class AssetLoader : Instance<AssetLoader>
	{
		private const int OBJ = 1;

		private const int CAM = 103;

		private HashSet<string> loadedIntersections = new HashSet<string>();

		private HashSet<string> hiddenAssets = new HashSet<string>();

		private readonly int[] loadQueueIndex = new int[12]
		{
			3, 1, 0, 4, 4, 3, 3, 1, 0, 2,
			2, 2
		};

		private readonly CustomAssetMetaData.Type[] typeMap = new CustomAssetMetaData.Type[12]
		{
			CustomAssetMetaData.Type.Building,
			CustomAssetMetaData.Type.Prop,
			CustomAssetMetaData.Type.Tree,
			CustomAssetMetaData.Type.Vehicle,
			CustomAssetMetaData.Type.Vehicle,
			CustomAssetMetaData.Type.Building,
			CustomAssetMetaData.Type.Building,
			CustomAssetMetaData.Type.Prop,
			CustomAssetMetaData.Type.Citizen,
			CustomAssetMetaData.Type.Road,
			CustomAssetMetaData.Type.Road,
			CustomAssetMetaData.Type.Building
		};

		private Dictionary<Package, CustomAssetMetaData.Type> packageTypes = new Dictionary<Package, CustomAssetMetaData.Type>(256);

		private Dictionary<string, SomeMetaData> metaDatas = new Dictionary<string, SomeMetaData>(128);

		private Dictionary<string, CustomAssetMetaData> citizenMetaDatas = new Dictionary<string, CustomAssetMetaData>();

		private Dictionary<string, List<Package.Asset>>[] suspects;

		private Dictionary<string, bool> boolValues;

		internal readonly Stack<Package.Asset> stack = new Stack<Package.Asset>(4);

		internal int beginMillis;

		internal int lastMillis;

		internal int assetCount;

		private float progress;

		private readonly bool recordAssets = Settings.settings.RecordAssets;

		private readonly bool checkAssets = Settings.settings.checkAssets;

		private readonly bool hasAssetDataExtensions;

		internal const int yieldInterval = 350;

		internal Package.Asset Current
		{
			get
			{
				if (stack.Count <= 0)
				{
					return null;
				}
				return stack.Peek();
			}
		}

		internal bool IsIntersection(string fullName)
		{
			return loadedIntersections.Contains(fullName);
		}

		private AssetLoader()
		{
			Dictionary<string, List<Package.Asset>> dictionary = new Dictionary<string, List<Package.Asset>>(4);
			Dictionary<string, List<Package.Asset>> dictionary2 = new Dictionary<string, List<Package.Asset>>(4);
			Dictionary<string, List<Package.Asset>> dictionary3 = new Dictionary<string, List<Package.Asset>>(4);
			Dictionary<string, List<Package.Asset>> dictionary4 = new Dictionary<string, List<Package.Asset>>(4);
			Dictionary<string, List<Package.Asset>> dictionary5 = new Dictionary<string, List<Package.Asset>>(4);
			Dictionary<string, List<Package.Asset>> dictionary6 = new Dictionary<string, List<Package.Asset>>(4);
			suspects = new Dictionary<string, List<Package.Asset>>[12]
			{
				dictionary, dictionary2, dictionary3, dictionary4, dictionary4, dictionary, dictionary, dictionary2, dictionary5, dictionary6,
				dictionary6, dictionary
			};
			boolValues = (Dictionary<string, bool>)Util.Get(GameSettings.FindSettingsFileByName(PackageManager.assetStateSettingsFile), "m_SettingsBoolValues");
			List<IAssetDataExtension> list = (List<IAssetDataExtension>)Util.Get(Singleton<LoadingManager>.instance.m_AssetDataWrapper, "m_AssetDataExtensions");
			hasAssetDataExtensions = list.Count > 0;
			if (hasAssetDataExtensions)
			{
				Util.DebugPrint("IAssetDataExtensions:", list.Count);
			}
		}

		public void Setup()
		{
			Instance<CustomDeserializer>.instance.Setup();
			Instance<Sharing>.Create();
			if (recordAssets)
			{
				Instance<Reports>.Create();
			}
			if (Settings.settings.hideAssets)
			{
				Settings.settings.LoadHiddenAssets(hiddenAssets);
			}
		}

		public void Dispose()
		{
			if (Settings.settings.reportAssets)
			{
				Instance<Reports>.instance.SaveStats();
			}
			if (recordAssets)
			{
				Instance<Reports>.instance.Dispose();
			}
			Instance<UsedAssets>.instance?.Dispose();
			Instance<Sharing>.instance?.Dispose();
			loadedIntersections.Clear();
			hiddenAssets.Clear();
			packageTypes.Clear();
			metaDatas.Clear();
			citizenMetaDatas.Clear();
			loadedIntersections = null;
			hiddenAssets = null;
			packageTypes = null;
			metaDatas = null;
			citizenMetaDatas = null;
			Array.Clear(suspects, 0, suspects.Length);
			suspects = null;
			boolValues = null;
			Instance<AssetLoader>.instance = null;
		}

		private void Report()
		{
			Settings s = Settings.settings;
			if (s.loadUsed)
			{
				Instance<UsedAssets>.instance.ReportMissingAssets();
			}
			if (recordAssets)
			{
				if (s.reportAssets)
				{
					Instance<Reports>.instance.Save(hiddenAssets, Instance<Sharing>.instance.texhit, Instance<Sharing>.instance.mathit, Instance<Sharing>.instance.meshit);
				}
				if (s.hideAssets)
				{
					s.SaveHiddenAssets(hiddenAssets, Instance<Reports>.instance.GetMissing(), Instance<Reports>.instance.GetDuplicates());
				}
				if (!s.enableDisable)
				{
					Instance<Reports>.instance.ClearAssets();
				}
			}
			Instance<Sharing>.instance.Dispose();
		}

		public IEnumerator LoadCustomContent()
		{
			Singleton<LoadingManager>.instance.m_loadingProfilerMain.BeginLoading("LoadCustomContent");
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.Reset();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.Reset();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.BeginLoading("District Styles");
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.PauseLoading();
			Instance<LevelLoader>.instance.assetsStarted = true;
			List<DistrictStyle> districtStyles = new List<DistrictStyle>();
			HashSet<string> assetnames = new HashSet<string>();
			FastList<DistrictStyleMetaData> districtStyleMetaDatas = new FastList<DistrictStyleMetaData>();
			FastList<Package> districtStylePackages = new FastList<Package>();
			Package.Asset styleAsset = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanStyleName);
			if (styleAsset != null && styleAsset.isEnabled)
			{
				DistrictStyle districtStyle = new DistrictStyle(DistrictStyle.kEuropeanStyleName, builtIn: true);
				Util.InvokeVoid(Singleton<LoadingManager>.instance, "AddChildrenToBuiltinStyle", GameObject.Find("European Style new"), districtStyle, false);
				Util.InvokeVoid(Singleton<LoadingManager>.instance, "AddChildrenToBuiltinStyle", GameObject.Find("European Style others"), districtStyle, true);
				if (Settings.settings.SkipPrefabs)
				{
					PrefabLoader.RemoveSkippedFromStyle(districtStyle);
				}
				districtStyles.Add(districtStyle);
			}
			if (LevelLoader.Check(715190))
			{
				Package.Asset asset2 = PackageManager.FindAssetByName("System." + DistrictStyle.kEuropeanSuburbiaStyleName);
				if (asset2 != null && asset2.isEnabled)
				{
					DistrictStyle districtStyle = new DistrictStyle(DistrictStyle.kEuropeanSuburbiaStyleName, builtIn: true);
					Util.InvokeVoid(Singleton<LoadingManager>.instance, "AddChildrenToBuiltinStyle", GameObject.Find("Modder Pack 3"), districtStyle, false);
					if (Settings.settings.SkipPrefabs)
					{
						PrefabLoader.RemoveSkippedFromStyle(districtStyle);
					}
					districtStyles.Add(districtStyle);
				}
			}
			if (LevelLoader.Check(1148020))
			{
				Package.Asset asset3 = PackageManager.FindAssetByName("System." + DistrictStyle.kModderPack5StyleName);
				if (asset3 != null && asset3.isEnabled)
				{
					DistrictStyle districtStyle = new DistrictStyle(DistrictStyle.kModderPack5StyleName, builtIn: true);
					Util.InvokeVoid(Singleton<LoadingManager>.instance, "AddChildrenToBuiltinStyle", GameObject.Find("Modder Pack 5"), districtStyle, false);
					if (Settings.settings.SkipPrefabs)
					{
						PrefabLoader.RemoveSkippedFromStyle(districtStyle);
					}
					districtStyles.Add(districtStyle);
				}
			}
			if (Settings.settings.SkipPrefabs)
			{
				PrefabLoader.UnloadSkipped();
			}
			foreach (Package.Asset item in PackageManager.FilterAssets(UserAssetType.DistrictStyleMetaData))
			{
				try
				{
					if (!(item != null) || !item.isEnabled)
					{
						continue;
					}
					DistrictStyleMetaData districtStyleMetaData = item.Instantiate<DistrictStyleMetaData>();
					if (districtStyleMetaData == null || districtStyleMetaData.builtin)
					{
						continue;
					}
					districtStyleMetaDatas.Add(districtStyleMetaData);
					districtStylePackages.Add(item.package);
					if (districtStyleMetaData.assets != null)
					{
						for (int k = 0; k < districtStyleMetaData.assets.Length; k++)
						{
							assetnames.Add(districtStyleMetaData.assets[k]);
						}
					}
				}
				catch (Exception ex)
				{
					CODebugBase<LogChannel>.Warn(LogChannel.Modding, string.Concat(ex.GetType(), ": Loading custom district style failed[", item, "]\n", ex.Message));
				}
			}
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.ContinueLoading();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.EndLoading();
			if (Settings.settings.loadUsed)
			{
				Instance<UsedAssets>.Create();
			}
			Instance<LoadingScreen>.instance.DualSource.Add(L10n.Get(136));
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.BeginLoading("Calculating asset load order");
			PrintMem();
			Package.Asset[] loadQueue = GetLoadQueue(assetnames);
			Util.DebugPrint("LoadQueue", loadQueue.Length, Profiling.Millis);
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.EndLoading();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.BeginLoading("Loading Custom Assets");
			Instance<Sharing>.instance.Start(loadQueue);
			beginMillis = (lastMillis = Profiling.Millis);
			for (int k = 0; k < loadQueue.Length; k++)
			{
				if ((k & 0x3F) == 0)
				{
					PrintMem(k);
				}
				Instance<Sharing>.instance.WaitForWorkers(k);
				stack.Clear();
				Package.Asset asset = loadQueue[k];
				try
				{
					LoadImpl(asset);
				}
				catch (Exception e)
				{
					AssetFailed(asset, asset.package, e);
				}
				if (Profiling.Millis - lastMillis > 350)
				{
					lastMillis = Profiling.Millis;
					progress = 0.15f + (float)(k + 1) * 0.7f / (float)loadQueue.Length;
					Instance<LoadingScreen>.instance.SetProgress(progress, progress, assetCount, assetCount - k - 1 + loadQueue.Length, beginMillis, lastMillis);
					yield return null;
				}
			}
			lastMillis = Profiling.Millis;
			Instance<LoadingScreen>.instance.SetProgress(0.85f, 1f, assetCount, assetCount, beginMillis, lastMillis);
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.EndLoading();
			Util.DebugPrint(assetCount, "custom assets loaded in", lastMillis - beginMillis);
			Instance<CustomDeserializer>.instance.SetCompleted();
			PrintMem();
			stack.Clear();
			Report();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.BeginLoading("Finalizing District Styles");
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.PauseLoading();
			for (int k = 0; k < districtStyleMetaDatas.m_size; k++)
			{
				try
				{
					DistrictStyleMetaData districtStyleMetaData = districtStyleMetaDatas.m_buffer[k];
					DistrictStyle districtStyle = new DistrictStyle(districtStyleMetaData.name, builtIn: false);
					if (districtStylePackages.m_buffer[k].GetPublishedFileID() != PublishedFileId.invalid)
					{
						districtStyle.PackageName = districtStylePackages.m_buffer[k].packageName;
					}
					if (districtStyleMetaData.assets == null)
					{
						continue;
					}
					for (int l = 0; l < districtStyleMetaData.assets.Length; l++)
					{
						BuildingInfo buildingInfo = CustomDeserializer.FindLoaded<BuildingInfo>(districtStyleMetaData.assets[l] + "_Data");
						if (buildingInfo != null)
						{
							districtStyle.Add(buildingInfo);
							if (districtStyleMetaData.builtin)
							{
								buildingInfo.m_dontSpawnNormally = !districtStyleMetaData.assetRef.isEnabled;
							}
						}
						else
						{
							CODebugBase<LogChannel>.Warn(LogChannel.Modding, "Warning: Missing asset (" + districtStyleMetaData.assets[l] + ") in style " + districtStyleMetaData.name);
						}
					}
					districtStyles.Add(districtStyle);
				}
				catch (Exception ex2)
				{
					CODebugBase<LogChannel>.Warn(LogChannel.Modding, ex2.GetType()?.ToString() + ": Loading district style failed\n" + ex2.Message);
				}
			}
			Singleton<DistrictManager>.instance.m_Styles = districtStyles.ToArray();
			if (Singleton<BuildingManager>.exists)
			{
				Singleton<BuildingManager>.instance.InitializeStyleArray(districtStyles.Count);
			}
			if (Settings.settings.enableDisable)
			{
				Util.DebugPrint("Going to enable and disable assets");
				Instance<LoadingScreen>.instance.DualSource.Add(L10n.Get(137));
				yield return null;
				EnableDisableAssets();
			}
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.ContinueLoading();
			Singleton<LoadingManager>.instance.m_loadingProfilerCustomContent.EndLoading();
			Singleton<LoadingManager>.instance.m_loadingProfilerMain.EndLoading();
			Instance<LevelLoader>.instance.assetsFinished = true;
		}

		internal static void PrintMem(int i = -1)
		{
			string fullName = ((i >= 0) ? ("[LSM] Mem " + i + ": ") : "[LSM] Mem ") + Profiling.Millis;
			try
			{
				if (Application.platform == RuntimePlatform.WindowsPlayer)
				{
					MemoryAPI.GetUsage(out var pfMegas, out var wsMegas);
					fullName = fullName + " " + wsMegas + " " + pfMegas;
				}
				if (Instance<Sharing>.HasInstance)
				{
					fullName = fullName + " " + Instance<Sharing>.instance.Status + " " + Instance<Sharing>.instance.Misses + " " + Instance<Sharing>.instance.LoaderAhead;
				}
			}
			catch (Exception)
			{
			}
			Console.WriteLine(fullName);
		}

		internal void LoadImpl(Package.Asset assetRef)
		{
			try
			{
				stack.Push(assetRef);
				string d = assetRef.name;
				Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.BeginLoading(ShortName(d));
				GameObject gameObject = AssetDeserializer.Instantiate(assetRef, isMain: true, isTop: true) as GameObject;
				if (gameObject == null)
				{
					throw new Exception(assetRef.fullName + ": no GameObject");
				}
				Package a1 = assetRef.package;
				CustomAssetMetaData.Type b1;
				bool num = packageTypes.TryGetValue(a1, out b1);
				bool flag = b1 == CustomAssetMetaData.Type.Road;
				if (checkAssets && d != gameObject.name)
				{
					Instance<Reports>.instance.AddNamingConflict(a1);
				}
				string text2 = (gameObject.name = (((num && !flag) || !d.Contains(".") || !IsPillarOrElevation(assetRef, flag)) ? assetRef.fullName : PillarOrElevationName(a1.packageName, d)));
				gameObject.SetActive(value: false);
				PrefabInfo prefabInfo = gameObject.GetComponent<PrefabInfo>();
				prefabInfo.m_isCustomContent = true;
				if (prefabInfo.m_Atlas != null && !string.IsNullOrEmpty(prefabInfo.m_InfoTooltipThumbnail))
				{
					prefabInfo.m_InfoTooltipAtlas = prefabInfo.m_Atlas;
				}
				PropInfo component;
				TreeInfo component2;
				BuildingInfo component3;
				VehicleInfo component4;
				CitizenInfo component5;
				NetInfo component6;
				if ((component = gameObject.GetComponent<PropInfo>()) != null)
				{
					if (component.m_lodObject != null)
					{
						component.m_lodObject.SetActive(value: false);
					}
					Initialize(component);
				}
				else if ((component2 = gameObject.GetComponent<TreeInfo>()) != null)
				{
					Initialize(component2);
				}
				else if ((component3 = gameObject.GetComponent<BuildingInfo>()) != null)
				{
					if (component3.m_lodObject != null)
					{
						component3.m_lodObject.SetActive(value: false);
					}
					if (a1.version < 7)
					{
						LegacyMetroUtils.PatchBuildingPaths(component3);
					}
					Initialize(component3);
					if (component3.GetAI() is IntersectionAI)
					{
						loadedIntersections.Add(text2);
					}
				}
				else if ((component4 = gameObject.GetComponent<VehicleInfo>()) != null)
				{
					if (component4.m_lodObject != null)
					{
						component4.m_lodObject.SetActive(value: false);
					}
					Initialize(component4);
				}
				else if ((component5 = gameObject.GetComponent<CitizenInfo>()) != null)
				{
					if (component5.m_lodObject != null)
					{
						component5.m_lodObject.SetActive(value: false);
					}
					if (citizenMetaDatas.TryGetValue(text2, out var value))
					{
						citizenMetaDatas.Remove(text2);
					}
					else
					{
						value = GetMetaDataFor(assetRef);
					}
					if (value != null && component5.InitializeCustomPrefab(value))
					{
						component5.gameObject.SetActive(value: true);
						Initialize(component5);
					}
					else
					{
						prefabInfo = null;
						CODebugBase<LogChannel>.Warn(LogChannel.Modding, "Custom citizen [" + text2 + "] template not available in selected theme. Asset not added in game.");
					}
				}
				else if ((component6 = gameObject.GetComponent<NetInfo>()) != null)
				{
					Initialize(component6);
				}
				else
				{
					prefabInfo = null;
				}
				if (hasAssetDataExtensions && prefabInfo != null)
				{
					CallExtensions(assetRef, prefabInfo);
				}
			}
			finally
			{
				stack.Pop();
				assetCount++;
				Singleton<LoadingManager>.instance.m_loadingProfilerCustomAsset.EndLoading();
			}
		}

		private void Initialize<T>(T info) where T : PrefabInfo
		{
			string brokenAssets = Singleton<LoadingManager>.instance.m_brokenAssets;
			PrefabCollection<T>.InitializePrefabs("Custom Assets", info, null);
			Singleton<LoadingManager>.instance.m_brokenAssets = brokenAssets;
			string packages = info.name;
			if ((UnityEngine.Object)CustomDeserializer.FindLoaded<T>(packages, tryName: false) == (UnityEngine.Object)null)
			{
				throw new Exception(typeof(T).Name + " " + packages + " failed");
			}
		}

		private int PackageComparison(Package a, Package b)
		{
			int assetRef = string.Compare(a.packageName, b.packageName);
			if (assetRef != 0)
			{
				return assetRef;
			}
			Package.Asset package = a.Find(a.packageMainAsset);
			Package.Asset fullName = b.Find(b.packageMainAsset);
			if ((package == null) | (fullName == null))
			{
				return 0;
			}
			bool flag = IsEnabled(package);
			if (flag != IsEnabled(fullName))
			{
				if (!flag)
				{
					return 1;
				}
				return -1;
			}
			return (int)fullName.offset - (int)package.offset;
		}

		private Package.Asset[] GetLoadQueue(HashSet<string> styleBuildings)
		{
			Package[] customPackages = new Package[0];
			try
			{
				customPackages = PackageManager.allPackages.Where((Package p) => p.FilterAssets(UserAssetType.CustomAssetMetaData).Any()).ToArray();
				Array.Sort(customPackages, PackageComparison);
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
			List<Package.Asset>[] assetQueues = new List<Package.Asset>[5]
			{
				new List<Package.Asset>(32),
				new List<Package.Asset>(128),
				new List<Package.Asset>(32),
				new List<Package.Asset>(128),
				new List<Package.Asset>(64)
			};
			List<Package.Asset> assets = new List<Package.Asset>(8);
			HashSet<string> prevAssetNames = new HashSet<string>();
			string index = string.Empty;
			SteamHelper.DLC_BitMask indices = ~SteamHelper.GetOwnedDLCMask();
			bool loadEnabled = Settings.settings.loadEnabled & !Settings.settings.enableDisable;
			bool loadUsed = Settings.settings.loadUsed;
			Package[] array2 = customPackages;
			foreach (Package package in array2)
			{
				Package.Asset lastAsset = null;
				try
				{
					Instance<CustomDeserializer>.instance.AddPackage(package);
					Package.Asset mainAsset = package.Find(package.packageMainAsset);
					string packageName = package.packageName;
					bool shouldLoadEnabledAsset = (loadEnabled && IsEnabled(mainAsset)) || styleBuildings.Contains(mainAsset.fullName);
					if (!shouldLoadEnabledAsset && (!loadUsed || !Instance<UsedAssets>.instance.GotPackage(packageName)))
					{
						continue;
					}
					CustomAssetMetaData assetMetadata = GetAssetRefs(mainAsset, assets);
					int count = assets.Count;
					lastAsset = assets[count - 1];
					CustomAssetMetaData.Type type = typeMap[(int)assetMetadata.type];
					packageTypes.Add(package, type);
					bool shouldLoadUsedasset = loadUsed && Instance<UsedAssets>.instance.IsUsed(lastAsset, type);
					shouldLoadEnabledAsset = shouldLoadEnabledAsset && (AssetImporterAssetTemplate.GetAssetDLCMask(assetMetadata) & indices) == 0;
					if (count > 1 && !shouldLoadUsedasset && loadUsed)
					{
						for (int j = 0; j < count - 1; j++)
						{
							if ((type != CustomAssetMetaData.Type.Road && Instance<UsedAssets>.instance.IsUsed(assets[j], type)) || (type == CustomAssetMetaData.Type.Road && Instance<UsedAssets>.instance.IsUsed(assets[j], CustomAssetMetaData.Type.Road, CustomAssetMetaData.Type.Building)))
							{
								shouldLoadUsedasset = true;
								break;
							}
						}
					}
					if (!(shouldLoadEnabledAsset || shouldLoadUsedasset))
					{
						continue;
					}
					if (recordAssets)
					{
						Instance<Reports>.instance.AddPackage(lastAsset, type, shouldLoadEnabledAsset, shouldLoadUsedasset);
					}
					if (packageName != index)
					{
						index = packageName;
						prevAssetNames.Clear();
					}
					List<Package.Asset> queue = assetQueues[loadQueueIndex[(int)type]];
					for (int k = 0; k < count - 1; k++)
					{
						Package.Asset asset = assets[k];
						if (prevAssetNames.Add(asset.name) || !IsDuplicate(asset, type, assetQueues, isMainAssetRef: false))
						{
							queue.Add(asset);
						}
					}
					if (prevAssetNames.Add(lastAsset.name) || !IsDuplicate(lastAsset, type, assetQueues, isMainAssetRef: true))
					{
						queue.Add(lastAsset);
						if (hasAssetDataExtensions)
						{
							metaDatas[lastAsset.fullName] = new SomeMetaData(assetMetadata.userDataRef, assetMetadata.name);
						}
						if (type == CustomAssetMetaData.Type.Citizen)
						{
							citizenMetaDatas[lastAsset.fullName] = assetMetadata;
						}
					}
				}
				catch (Exception e)
				{
					AssetFailed(lastAsset, package, e);
				}
			}
			CheckSuspects();
			prevAssetNames.Clear();
			prevAssetNames = null;
			Package.Asset[] finalQueue = new Package.Asset[assetQueues.Sum(queue_ => queue_.Count)];
			int l = 0;
			int pointer = 0;
			for (; l < assetQueues.Length; l++)
			{
				assetQueues[l].CopyTo(finalQueue, pointer);
				pointer += assetQueues[l].Count;
				assetQueues[l].Clear();
				assetQueues[l] = null;
			}
			assetQueues = null;
			return finalQueue;
		}

		private static CustomAssetMetaData GetAssetRefs(Package.Asset mainAsset, List<Package.Asset> assetRefs)
		{
			CustomAssetMetaData customAssetMetaData = AssetDeserializer.InstantiateOne(mainAsset) as CustomAssetMetaData;
			Package.Asset kvp = customAssetMetaData.assetRef;
			Package.Asset assets = null;
			assetRefs.Clear();
			foreach (Package.Asset item in mainAsset.package)
			{
				switch ((int)item.type)
				{
				case 1:
					assets = item;
					if ((object)item != kvp)
					{
						break;
					}
					goto end_IL_0029;
				case 103:
					if (assets != null)
					{
						string name = assets.name;
						int length = name.Length;
						if (length < 35 || name[length - 34] != '-' || name[length - 35] != ' ' || name[length - 33] != ' ')
						{
							assetRefs.Add(assets);
							assets = null;
							break;
						}
						GetSecondaryAssetRefs(mainAsset, assetRefs);
					}
					else
					{
						GetSecondaryAssetRefs(mainAsset, assetRefs);
					}
					goto end_IL_0029;
				}
			}
			end_IL_0029:
			if (kvp != null)
			{
				assetRefs.Add(kvp);
			}
			return customAssetMetaData;
		}

		private static void GetSecondaryAssetRefs(Package.Asset mainAsset, List<Package.Asset> assetRefs)
		{
			Util.DebugPrint("!GetSecondaryAssetRefs", mainAsset.fullName);
			assetRefs.Clear();
			foreach (Package.Asset item in mainAsset.package.FilterAssets(UserAssetType.CustomAssetMetaData))
			{
				if ((object)item != mainAsset)
				{
					Package.Asset assetRef = (AssetDeserializer.InstantiateOne(item) as CustomAssetMetaData).assetRef;
					if (assetRef != null)
					{
						assetRefs.Add(assetRef);
						continue;
					}
					Util.DebugPrint("!NULL asset", mainAsset.fullName);
				}
			}
		}

		private CustomAssetMetaData.Type GetMetaTypeFor(Package.Asset assetRef, CustomAssetMetaData.Type packageType)
		{
			if (packageType != CustomAssetMetaData.Type.Road || IsMainAssetRef(assetRef))
			{
				return packageType;
			}
			return typeMap[(int)GetMetaDataFor(assetRef).type];
		}

		private CustomAssetMetaData.Type GetMetaTypeFor(Package.Asset assetRef, CustomAssetMetaData.Type packageType, bool isMainAssetRef)
		{
			if (isMainAssetRef || packageType != CustomAssetMetaData.Type.Road)
			{
				return packageType;
			}
			return typeMap[(int)GetMetaDataFor(assetRef).type];
		}

		private static CustomAssetMetaData GetMetaDataFor(Package.Asset assetRef)
		{
			bool flag = true;
			foreach (Package.Asset meta in assetRef.package)
			{
				if (flag)
				{
					if ((object)meta == assetRef)
					{
						flag = false;
					}
				}
				else if (meta.type.m_Value == 103)
				{
					CustomAssetMetaData customAssetMetaData = AssetDeserializer.InstantiateOne(meta) as CustomAssetMetaData;
					if ((object)customAssetMetaData.assetRef == assetRef)
					{
						return customAssetMetaData;
					}
					break;
				}
			}
			Util.DebugPrint("!assetRef mismatch", assetRef.fullName);
			foreach (Package.Asset item in assetRef.package.FilterAssets(UserAssetType.CustomAssetMetaData))
			{
				CustomAssetMetaData customAssetMetaData2 = AssetDeserializer.InstantiateOne(item) as CustomAssetMetaData;
				if ((object)customAssetMetaData2.assetRef == assetRef)
				{
					return customAssetMetaData2;
				}
			}
			Util.DebugPrint("!Cannot get metadata for", assetRef.fullName);
			return null;
		}

		private static CustomAssetMetaData GetMainMetaDataFor(Package p)
		{
			Package.Asset asset = p.Find(p.packageMainAsset);
			if (!(asset != null))
			{
				return null;
			}
			return AssetDeserializer.InstantiateOne(asset) as CustomAssetMetaData;
		}

		internal CustomAssetMetaData.Type GetPackageTypeFor(Package p)
		{
			if (packageTypes.TryGetValue(p, out var value))
			{
				return value;
			}
			CustomAssetMetaData mainMetaDataFor = GetMainMetaDataFor(p);
			if (mainMetaDataFor != null)
			{
				value = typeMap[(int)mainMetaDataFor.type];
				packageTypes.Add(p, value);
				return value;
			}
			Util.DebugPrint("!Cannot get package type for", p.packagePath);
			return CustomAssetMetaData.Type.Building;
		}

		private bool IsDuplicate(Package.Asset assetRef, CustomAssetMetaData.Type packageType, List<Package.Asset>[] queues, bool isMainAssetRef)
		{
			CustomAssetMetaData.Type metaTypeFor = GetMetaTypeFor(assetRef, packageType, isMainAssetRef);
			Dictionary<string, List<Package.Asset>> dictionary = suspects[(int)metaTypeFor];
			string fullName = assetRef.fullName;
			if (dictionary.TryGetValue(fullName, out var value))
			{
				value.Add(assetRef);
			}
			else
			{
				value = new List<Package.Asset>(2);
				FindDuplicates(assetRef, metaTypeFor, queues[loadQueueIndex[(int)metaTypeFor]], value);
				if (metaTypeFor == CustomAssetMetaData.Type.Building)
				{
					FindDuplicates(assetRef, metaTypeFor, queues[loadQueueIndex[9]], value);
				}
				if (value.Count == 0)
				{
					return false;
				}
				value.Add(assetRef);
				dictionary.Add(fullName, value);
			}
			return true;
		}

		private void FindDuplicates(Package.Asset assetRef, CustomAssetMetaData.Type type, List<Package.Asset> q, List<Package.Asset> assets)
		{
			string i = assetRef.name;
			string packageName = assetRef.package.packageName;
			int num = q.Count - 1;
			while (num >= 0)
			{
				Package.Asset asset = q[num];
				Package package = asset.package;
				if (!(package.packageName != packageName))
				{
					if (asset.name == i && GetMetaTypeFor(asset, packageTypes[package]) == type)
					{
						assets.Insert(0, asset);
					}
					num--;
					continue;
				}
				break;
			}
		}

		private void CheckSuspects()
		{
			CustomAssetMetaData.Type[] fullName = new CustomAssetMetaData.Type[6]
			{
				CustomAssetMetaData.Type.Building,
				CustomAssetMetaData.Type.Prop,
				CustomAssetMetaData.Type.Tree,
				CustomAssetMetaData.Type.Vehicle,
				CustomAssetMetaData.Type.Citizen,
				CustomAssetMetaData.Type.Road
			};
			foreach (CustomAssetMetaData.Type num in fullName)
			{
				foreach (KeyValuePair<string, List<Package.Asset>> item in suspects[(int)num])
				{
					List<Package.Asset> value = item.Value;
					if (value.Select((Package.Asset a) => a.checksum).Distinct().Count() > 1 && value.Where((Package.Asset a) => IsEnabled(a.package)).Count() != 1)
					{
						Duplicate(item.Key, value);
					}
				}
			}
		}

		private bool IsEnabled(Package package)
		{
			Package.Asset asset = package.Find(package.packageMainAsset);
			if (!(asset == null))
			{
				return IsEnabled(asset);
			}
			return true;
		}

		private bool IsEnabled(Package.Asset mainAsset)
		{
			bool profiler;
			return !boolValues.TryGetValue(mainAsset.checksum + ".enabled", out profiler) || profiler;
		}

		private void CallExtensions(Package.Asset assetRef, PrefabInfo info)
		{
			string fullName = assetRef.fullName;
			if (metaDatas.TryGetValue(fullName, out var value))
			{
				metaDatas.Remove(fullName);
			}
			else if (IsMainAssetRef(assetRef))
			{
				CustomAssetMetaData mainMetaDataFor = GetMainMetaDataFor(assetRef.package);
				value = new SomeMetaData(mainMetaDataFor.userDataRef, mainMetaDataFor.name);
			}
			if (value.userDataRef != null)
			{
				AssetDataWrapper.UserAssetData userAssetData = AssetDeserializer.InstantiateOne(value.userDataRef) as AssetDataWrapper.UserAssetData;
				if (userAssetData == null)
				{
					userAssetData = new AssetDataWrapper.UserAssetData();
				}
				Singleton<LoadingManager>.instance.m_AssetDataWrapper.OnAssetLoaded(value.name, info, userAssetData);
			}
		}

		private static bool IsPillarOrElevation(Package.Asset assetRef, bool knownRoad)
		{
			if (knownRoad)
			{
				return !IsMainAssetRef(assetRef);
			}
			int num = 0;
			foreach (Package.Asset item in assetRef.package.FilterAssets(UserAssetType.CustomAssetMetaData))
			{
				_ = item;
				if (++num > 1)
				{
					break;
				}
			}
			if (num != 1)
			{
				return GetMetaDataFor(assetRef).type >= CustomAssetMetaData.Type.RoadElevation;
			}
			return false;
		}

		private static string PillarOrElevationName(string packageName, string name)
		{
			return packageName + "." + PackageHelper.StripName(name);
		}

		internal static Package.Asset FindMainAssetRef(Package p)
		{
			return p.FilterAssets(Package.AssetType.Object).LastOrDefault((Package.Asset a) => a.name.EndsWith("_Data"));
		}

		private static bool IsMainAssetRef(Package.Asset assetRef)
		{
			return (object)FindMainAssetRef(assetRef.package) == assetRef;
		}

		internal static string ShortName(string name_Data)
		{
			if (name_Data.Length <= 5 || !name_Data.EndsWith("_Data"))
			{
				return name_Data;
			}
			return name_Data.Substring(0, name_Data.Length - 5);
		}

		private static string ShortAssetName(string fullName_Data)
		{
			int mainAssetRef = fullName_Data.IndexOf('.');
			if (mainAssetRef >= 0 && mainAssetRef < fullName_Data.Length - 1)
			{
				fullName_Data = fullName_Data.Substring(mainAssetRef + 1);
			}
			return ShortName(fullName_Data);
		}

		internal void AssetFailed(Package.Asset assetRef, Package p, Exception e)
		{
			string text = assetRef?.fullName;
			if (text == null)
			{
				assetRef = FindMainAssetRef(p);
				text = assetRef?.fullName;
			}
			if (text != null && Instance<LevelLoader>.instance.AddFailed(text))
			{
				if (recordAssets)
				{
					Instance<Reports>.instance.AssetFailed(assetRef);
				}
				Util.DebugPrint("Asset failed:", text);
				Instance<LoadingScreen>.instance.DualSource?.CustomAssetFailed(ShortAssetName(text));
			}
			if (e != null)
			{
				Debug.LogException(e);
			}
		}

		internal void NotFound(string fullName)
		{
			if (fullName != null && Instance<LevelLoader>.instance.AddFailed(fullName))
			{
				Util.DebugPrint("Missing:", fullName);
				if (!hiddenAssets.Contains(fullName))
				{
					Instance<LoadingScreen>.instance.DualSource?.CustomAssetNotFound(ShortAssetName(fullName));
				}
			}
		}

		private void Duplicate(string fullName, List<Package.Asset> assets)
		{
			if (recordAssets)
			{
				Instance<Reports>.instance.Duplicate(assets);
			}
			Util.DebugPrint("Duplicate name", fullName);
			if (!hiddenAssets.Contains(fullName))
			{
				Instance<LoadingScreen>.instance.DualSource?.CustomAssetDuplicate(ShortAssetName(fullName));
			}
		}

		private void EnableDisableAssets()
		{
			try
			{
				if (!Settings.settings.reportAssets)
				{
					Instance<Reports>.instance.SetIndirectUsages();
				}
				foreach (object item in Instance<CustomDeserializer>.instance.AllPackages())
				{
					Package package = item as Package;
					if ((object)package != null)
					{
						EnableDisableAssets(package);
						continue;
					}
					foreach (Package item2 in item as List<Package>)
					{
						EnableDisableAssets(item2);
					}
				}
				Instance<Reports>.instance.ClearAssets();
				GameSettings.FindSettingsFileByName(PackageManager.assetStateSettingsFile).MarkDirty();
			}
			catch (Exception exception)
			{
				Debug.LogException(exception);
			}
		}

		private void EnableDisableAssets(Package p)
		{
			bool flag = Instance<Reports>.instance.IsUsed(FindMainAssetRef(p));
			foreach (Package.Asset item2 in p.FilterAssets(UserAssetType.CustomAssetMetaData))
			{
				string key = item2.checksum + ".enabled";
				if (flag)
				{
					boolValues.Remove(key);
				}
				else
				{
					boolValues[key] = false;
				}
			}
		}
	}
}
