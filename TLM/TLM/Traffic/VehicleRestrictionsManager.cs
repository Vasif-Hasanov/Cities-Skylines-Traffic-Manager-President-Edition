﻿using ColossalFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TrafficManager.State;

namespace TrafficManager.Traffic {
	class VehicleRestrictionsManager {
		/// <summary>
		/// For each segment id and lane index: Holds the default set of vehicle types allowed for the lane
		/// </summary>
		private static ExtVehicleType?[][] defaultVehicleTypeCache = null;

		internal static void OnLevelUnloading() {
			defaultVehicleTypeCache = null;
		}

		/// <summary>
		/// Determines the allowed vehicle types that may approach the given node from the given segment.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="nodeId"></param>
		/// <returns></returns>
		internal static ExtVehicleType GetAllowedVehicleTypes(ushort segmentId, ushort nodeId) { // TODO optimize method (don't depend on collections!)
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetAllowedVehicleTypes(1)");
#endif
			ExtVehicleType ret = ExtVehicleType.None;
			foreach (ExtVehicleType vehicleType in GetAllowedVehicleTypesAsSet(segmentId, nodeId)) {
				ret |= vehicleType;
			}
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypes(1)");
#endif
			return ret;
		}

		/// <summary>
		/// Determines the allowed vehicle types that may approach the given node from the given segment.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="nodeId"></param>
		/// <returns></returns>
		internal static HashSet<ExtVehicleType> GetAllowedVehicleTypesAsSet(ushort segmentId, ushort nodeId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetAllowedVehicleTypesAsSet");
#endif
			HashSet<ExtVehicleType> ret = new HashSet<ExtVehicleType>(GetAllowedVehicleTypesAsDict(segmentId, nodeId).Values);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypesAsSet");
#endif
			return ret;
		}

		/// <summary>
		/// Determines the allowed vehicle types that may approach the given node from the given segment (lane-wise).
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="nodeId"></param>
		/// <returns></returns>
		internal static Dictionary<byte, ExtVehicleType> GetAllowedVehicleTypesAsDict(ushort segmentId, ushort nodeId) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetAllowedVehicleTypesAsDict");
#endif
			Dictionary<byte, ExtVehicleType> ret = new Dictionary<byte, ExtVehicleType>();

			NetManager netManager = Singleton<NetManager>.instance;
			if (segmentId == 0 || (netManager.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None ||
				nodeId == 0 || (netManager.m_nodes.m_buffer[nodeId].m_flags & NetNode.Flags.Created) == NetNode.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypesAsDict");
#endif
				return ret;
			}

			var dir = NetInfo.Direction.Forward;
			var dir2 = ((netManager.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Invert) == NetSegment.Flags.None) ? dir : NetInfo.InvertDirection(dir);
			var dir3 = TrafficPriority.IsLeftHandDrive() ? NetInfo.InvertDirection(dir2) : dir2;

			NetInfo segmentInfo = netManager.m_segments.m_buffer[segmentId].Info;
			uint curLaneId = netManager.m_segments.m_buffer[segmentId].m_lanes;
			int numLanes = segmentInfo.m_lanes.Length;
			uint laneIndex = 0;
			while (laneIndex < numLanes && curLaneId != 0u) {
				NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];
				ushort toNodeId = (laneInfo.m_direction == dir3) ? netManager.m_segments.m_buffer[segmentId].m_endNode : netManager.m_segments.m_buffer[segmentId].m_startNode;

				if (toNodeId == nodeId) {
					ExtVehicleType vehicleTypes = GetAllowedVehicleTypes(segmentId, segmentInfo, laneIndex, laneInfo);
					if (vehicleTypes != ExtVehicleType.None)
						ret[(byte)laneIndex] = vehicleTypes;
				}
				curLaneId = netManager.m_lanes.m_buffer[curLaneId].m_nextLane;
				++laneIndex;
			}

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypesAsDict");
#endif
			return ret;
		}

		/// <summary>
		/// Determines the allowed vehicle types for the given segment and lane.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="laneIndex"></param>
		/// <param name="segmetnInfo"></param>
		/// <param name="laneInfo"></param>
		/// <returns></returns>
		internal static ExtVehicleType GetAllowedVehicleTypes(ushort segmentId, NetInfo segmentInfo, uint laneIndex, NetInfo.Lane laneInfo) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetAllowedVehicleTypes(2)");
#endif
			if (Flags.IsInitDone()) {
				ExtVehicleType?[] fastArray = Flags.laneAllowedVehicleTypesArray[segmentId];
				if (fastArray != null && fastArray.Length > laneIndex && fastArray[laneIndex] != null) {
#if TRACE
					Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypes(2)");
#endif
					return (ExtVehicleType)fastArray[laneIndex];
				}
			}

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypes(2)");
#endif
			return GetDefaultAllowedVehicleTypes(segmentId, segmentInfo, laneIndex, laneInfo);
		}

		internal static bool HasSegmentRestrictions(ushort segmentId) { // TODO clean up restrictions (currently we do not check if restrictions are equal with the base type)
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetAllowedVehicleTypes(2)");
#endif
			if (Flags.IsInitDone()) {
				ExtVehicleType?[] fastArray = Flags.laneAllowedVehicleTypesArray[segmentId];
				return fastArray != null;
			}

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetAllowedVehicleTypes(2)");
#endif
			return false;
		}

		/// <summary>
		/// Determines the default set of allowed vehicle types for a given segment and lane.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="segmentInfo"></param>
		/// <param name="laneIndex"></param>
		/// <param name="laneInfo"></param>
		/// <returns></returns>
		public static ExtVehicleType GetDefaultAllowedVehicleTypes(ushort segmentId, NetInfo segmentInfo, uint laneIndex, NetInfo.Lane laneInfo) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.GetDefaultAllowedVehicleTypes");
#endif
			// manage cached default vehicle types
			if (defaultVehicleTypeCache == null) {
				defaultVehicleTypeCache = new ExtVehicleType?[NetManager.MAX_SEGMENT_COUNT][];
			}

			ExtVehicleType?[] cachedDefaultTypes = defaultVehicleTypeCache[segmentId];
			if (cachedDefaultTypes == null || cachedDefaultTypes.Length != segmentInfo.m_lanes.Length) {
				defaultVehicleTypeCache[segmentId] = cachedDefaultTypes = new ExtVehicleType?[segmentInfo.m_lanes.Length];
			}

			ExtVehicleType? defaultVehicleType = cachedDefaultTypes[laneIndex];
			if (defaultVehicleType == null) {
				ExtVehicleType ret = ExtVehicleType.None;
				if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Bicycle) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.Bicycle;
				if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Tram) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.Tram;
				if ((laneInfo.m_laneType & NetInfo.LaneType.TransportVehicle) != NetInfo.LaneType.None)
					ret |= ExtVehicleType.RoadPublicTransport | ExtVehicleType.Service | ExtVehicleType.Emergency;
				else if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Car) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.RoadVehicle;
				if ((laneInfo.m_vehicleType & (VehicleInfo.VehicleType.Train | VehicleInfo.VehicleType.Metro)) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.RailVehicle;
				if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Ship) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.Ship;
				if ((laneInfo.m_vehicleType & VehicleInfo.VehicleType.Plane) != VehicleInfo.VehicleType.None)
					ret |= ExtVehicleType.Plane;
				cachedDefaultTypes[laneIndex] = ret;
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetDefaultAllowedVehicleTypes");
#endif
				return ret;
			} else {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.GetDefaultAllowedVehicleTypes");
#endif
				return (ExtVehicleType)defaultVehicleType;
			}
		}

		/// <summary>
		/// Determines the default set of allowed vehicle types for a given lane.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="segmentInfo"></param>
		/// <param name="laneIndex"></param>
		/// <param name="laneInfo"></param>
		/// <returns></returns>
		internal static ExtVehicleType GetDefaultAllowedVehicleTypes(uint laneId) {
			if (((NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags & NetLane.Flags.Created) == NetLane.Flags.None)
				return ExtVehicleType.None;
			ushort segmentId = Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_segment;
			if ((Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
				return ExtVehicleType.None;

			NetInfo segmentInfo = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;
			uint curLaneId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_lanes;
			int numLanes = segmentInfo.m_lanes.Length;
			uint laneIndex = 0;
			while (laneIndex < numLanes && curLaneId != 0u) {
				NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];
				if (curLaneId == laneId) {
					return GetDefaultAllowedVehicleTypes(segmentId, segmentInfo, laneIndex, laneInfo);
				}
				curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
				++laneIndex;
			}

			return ExtVehicleType.None;
		}

		/// <summary>
		/// Sets the allowed vehicle types for the given segment and lane.
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="laneIndex"></param>
		/// <param name="laneId"></param>
		/// <param name="allowedTypes"></param>
		/// <returns></returns>
		internal static bool SetAllowedVehicleTypes(ushort segmentId, NetInfo segmentInfo, uint laneIndex, NetInfo.Lane laneInfo, uint laneId, ExtVehicleType allowedTypes) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.SetAllowedVehicleTypes");
#endif
			if (segmentId == 0 || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None || ((NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags & NetLane.Flags.Created) == NetLane.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.SetAllowedVehicleTypes");
#endif
				return false;
			}

			allowedTypes &= GetBaseMask(segmentInfo.m_lanes[laneIndex]); // ensure default base mask
			Flags.setLaneAllowedVehicleTypes(segmentId, laneIndex, laneId, allowedTypes);
			NotifyStartEndNode(segmentId);

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.SetAllowedVehicleTypes");
#endif
			return true;
		}

		/// <summary>
		/// Adds the given vehicle type to the set of allowed vehicles at the specified lane
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="laneIndex"></param>
		/// <param name="laneId"></param>
		/// <param name="laneInfo"></param>
		/// <param name="road"></param>
		/// <param name="vehicleType"></param>
		public static void AddAllowedType(ushort segmentId, NetInfo segmentInfo, uint laneIndex, uint laneId, NetInfo.Lane laneInfo, ExtVehicleType vehicleType) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.AddAllowedType");
#endif
			if (segmentId == 0 || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None || ((NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags & NetLane.Flags.Created) == NetLane.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.AddAllowedType");
#endif
				return;
			}

			ExtVehicleType allowedTypes = GetAllowedVehicleTypes(segmentId, segmentInfo, laneIndex, laneInfo);
			allowedTypes |= vehicleType;
			allowedTypes &= GetBaseMask(segmentInfo.m_lanes[laneIndex]); // ensure default base mask
			Flags.setLaneAllowedVehicleTypes(segmentId, laneIndex, laneId, allowedTypes);
			NotifyStartEndNode(segmentId);

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.AddAllowedType");
#endif
		}

		/// <summary>
		/// Removes the given vehicle type from the set of allowed vehicles at the specified lane
		/// </summary>
		/// <param name="segmentId"></param>
		/// <param name="laneIndex"></param>
		/// <param name="laneId"></param>
		/// <param name="laneInfo"></param>
		/// <param name="road"></param>
		/// <param name="vehicleType"></param>
		public static void RemoveAllowedType(ushort segmentId, NetInfo segmentInfo, uint laneIndex, uint laneId, NetInfo.Lane laneInfo, ExtVehicleType vehicleType) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.RemoveAllowedType");
#endif
			if (segmentId == 0 || (Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None || ((NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags & NetLane.Flags.Created) == NetLane.Flags.None) {
#if TRACE
				Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.RemoveAllowedType");
#endif
				return;
			}

			ExtVehicleType allowedTypes = GetAllowedVehicleTypes(segmentId, segmentInfo, laneIndex, laneInfo);
			allowedTypes &= ~vehicleType;
			allowedTypes &= GetBaseMask(segmentInfo.m_lanes[laneIndex]); // ensure default base mask
			Flags.setLaneAllowedVehicleTypes(segmentId, laneIndex, laneId, allowedTypes);
			NotifyStartEndNode(segmentId);

#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.RemoveAllowedType");
#endif
		}

		public static void ToggleAllowedType(ushort segmentId, NetInfo segmentInfo, uint laneIndex, uint laneId, NetInfo.Lane laneInfo, ExtVehicleType vehicleType, bool add) {
#if TRACE
			Singleton<CodeProfiler>.instance.Start("VehicleRestrictionsManager.ToggleAllowedType");
#endif
			if (add)
				AddAllowedType(segmentId, segmentInfo, laneIndex, laneId, laneInfo, vehicleType);
			else
				RemoveAllowedType(segmentId, segmentInfo, laneIndex, laneId, laneInfo, vehicleType);
#if TRACE
			Singleton<CodeProfiler>.instance.Stop("VehicleRestrictionsManager.ToggleAllowedType");
#endif
		}

		/// <summary>
		/// Determines the maximum allowed set of vehicles (the base mask) for a given lane
		/// </summary>
		/// <param name="laneInfo"></param>
		/// <returns></returns>
		public static ExtVehicleType GetBaseMask(NetInfo.Lane laneInfo) {
			if (IsRoadLane(laneInfo))
				return ExtVehicleType.RoadVehicle;
			else if (IsRailLane(laneInfo))
				return ExtVehicleType.RailVehicle;
			else
				return ExtVehicleType.None;
		}

		/// <summary>
		/// Determines the maximum allowed set of vehicles (the base mask) for a given lane
		/// </summary>
		/// <param name="laneInfo"></param>
		/// <returns></returns>
		public static ExtVehicleType GetBaseMask(uint laneId) {
			if (((NetLane.Flags)Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_flags & NetLane.Flags.Created) == NetLane.Flags.None)
				return ExtVehicleType.None;
			ushort segmentId = Singleton<NetManager>.instance.m_lanes.m_buffer[laneId].m_segment;
			if ((Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_flags & NetSegment.Flags.Created) == NetSegment.Flags.None)
				return ExtVehicleType.None;

			NetInfo segmentInfo = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].Info;
			uint curLaneId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_lanes;
			int numLanes = segmentInfo.m_lanes.Length;
			uint laneIndex = 0;
			while (laneIndex < numLanes && curLaneId != 0u) {
				NetInfo.Lane laneInfo = segmentInfo.m_lanes[laneIndex];
				if (curLaneId == laneId) {
					return GetBaseMask(laneInfo);
				}
				curLaneId = Singleton<NetManager>.instance.m_lanes.m_buffer[curLaneId].m_nextLane;
				++laneIndex;
			}
			return ExtVehicleType.None;
		}

		public static bool IsAllowed(ExtVehicleType? allowedTypes, ExtVehicleType vehicleType) {
			return allowedTypes == null || ((ExtVehicleType)allowedTypes & vehicleType) != ExtVehicleType.None;
		}

		public static bool IsBicycleAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Bicycle);
		}

		public static bool IsBusAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Bus);
		}

		public static bool IsCargoTrainAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.CargoTrain);
		}

		public static bool IsCargoTruckAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.CargoTruck);
		}

		public static bool IsEmergencyAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Emergency);
		}

		public static bool IsPassengerCarAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.PassengerCar);
		}

		public static bool IsPassengerTrainAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.PassengerTrain);
		}

		public static bool IsServiceAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Service);
		}

		public static bool IsTaxiAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Taxi);
		}

		public static bool IsTramAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.Tram);
		}

		public static bool IsRailVehicleAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.RailVehicle);
		}

		public static bool IsRoadVehicleAllowed(ExtVehicleType? allowedTypes) {
			return IsAllowed(allowedTypes, ExtVehicleType.RoadVehicle);
		}

		public static bool IsRailLane(NetInfo.Lane laneInfo) {
			return (laneInfo.m_vehicleType & VehicleInfo.VehicleType.Train) != VehicleInfo.VehicleType.None;
		}

		public static bool IsRoadLane(NetInfo.Lane laneInfo) {
			return (laneInfo.m_vehicleType & VehicleInfo.VehicleType.Car) != VehicleInfo.VehicleType.None;
		}

		public static bool IsRailSegment(NetInfo segmentInfo) {
			ItemClass connectionClass = segmentInfo.GetConnectionClass();
			return connectionClass.m_service == ItemClass.Service.PublicTransport && connectionClass.m_subService == ItemClass.SubService.PublicTransportTrain;
		}

		public static bool IsRoadSegment(NetInfo segmentInfo) {
			ItemClass connectionClass = segmentInfo.GetConnectionClass();
			return connectionClass.m_service == ItemClass.Service.Road;
		}

		internal static void ClearCache(ushort segmentId) {
			if (defaultVehicleTypeCache != null) {
				defaultVehicleTypeCache[segmentId] = null;
			}
		}

		public static void NotifyStartEndNode(ushort segmentId) {
			// notify observers of start node and end node
			ushort startNodeId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_startNode;
			ushort endNodeId = Singleton<NetManager>.instance.m_segments.m_buffer[segmentId].m_endNode;
			if (startNodeId != 0)
				NodeGeometry.Get(startNodeId).NotifyObservers();
			if (endNodeId != 0)
				NodeGeometry.Get(endNodeId).NotifyObservers();
		}
	}
}
