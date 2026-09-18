using System;
using System.Collections.Generic;

namespace Treadwell.Core
{
    public enum PavedRoadDiscoveryOutcome
    {
        None,
        Unique,
        Ambiguous
    }

    public sealed class PavedRoadCandidateShape
    {
        public PavedRoadCandidateShape(
            int pieceComponentCount,
            bool hasRootPiece,
            int terrainModifierCount,
            int pavedTerrainModifierCount,
            bool hasStationRequirement,
            int resourceRequirementCount,
            int singleUnitResourceRequirementCount,
            int singleUnitStoneResourceRequirementCount)
        {
            PieceComponentCount = pieceComponentCount;
            HasRootPiece = hasRootPiece;
            TerrainModifierCount = terrainModifierCount;
            PavedTerrainModifierCount = pavedTerrainModifierCount;
            HasStationRequirement = hasStationRequirement;
            ResourceRequirementCount = resourceRequirementCount;
            SingleUnitResourceRequirementCount = singleUnitResourceRequirementCount;
            SingleUnitStoneResourceRequirementCount = singleUnitStoneResourceRequirementCount;
        }

        public int PieceComponentCount { get; }
        public bool HasRootPiece { get; }
        public int TerrainModifierCount { get; }
        public int PavedTerrainModifierCount { get; }
        public bool HasStationRequirement { get; }
        public int ResourceRequirementCount { get; }
        public int SingleUnitResourceRequirementCount { get; }
        public int SingleUnitStoneResourceRequirementCount { get; }

        public bool IsSemanticCandidate =>
            PieceComponentCount == 1 &&
            HasRootPiece &&
            PavedTerrainModifierCount == 1 &&
            HasStationRequirement &&
            ResourceRequirementCount == 1 &&
            SingleUnitResourceRequirementCount == 1 &&
            SingleUnitStoneResourceRequirementCount == 1;
    }

    public sealed class PavedRoadCandidateSelection
    {
        internal PavedRoadCandidateSelection(PavedRoadDiscoveryOutcome outcome, int candidateIndex, int candidateCount)
        {
            Outcome = outcome;
            CandidateIndex = candidateIndex;
            CandidateCount = candidateCount;
        }

        public PavedRoadDiscoveryOutcome Outcome { get; }
        public int CandidateIndex { get; }
        public int CandidateCount { get; }
    }

    public static class PavedRoadCandidateSelector
    {
        public static PavedRoadCandidateSelection Select(IReadOnlyList<PavedRoadCandidateShape> shapes)
        {
            if (shapes == null) throw new ArgumentNullException(nameof(shapes));

            var candidateIndex = -1;
            var candidateCount = 0;
            for (var index = 0; index < shapes.Count; index++)
            {
                var shape = shapes[index];
                if (shape == null || !shape.IsSemanticCandidate) continue;
                candidateIndex = index;
                candidateCount++;
            }

            if (candidateCount == 0)
                return new PavedRoadCandidateSelection(PavedRoadDiscoveryOutcome.None, -1, 0);
            if (candidateCount == 1)
                return new PavedRoadCandidateSelection(PavedRoadDiscoveryOutcome.Unique, candidateIndex, 1);
            return new PavedRoadCandidateSelection(PavedRoadDiscoveryOutcome.Ambiguous, -1, candidateCount);
        }
    }

    public enum StationOverrideApplyResult
    {
        InvalidPiece,
        Removed,
        AlreadyAbsent,
        Conflict
    }

    public enum StationOverrideRestoreResult
    {
        NothingToRestore,
        Restored,
        AlreadyRestored,
        Conflict
    }

    public sealed class PavedRoadStationOverride<TPiece, TStation>
        where TPiece : class
        where TStation : class
    {
        private readonly Func<TPiece, TStation?> _getStation;
        private readonly Action<TPiece, TStation?> _setStation;
        private TPiece? _piece;
        private TStation? _originalStation;

        public PavedRoadStationOverride(
            Func<TPiece, TStation?> getStation,
            Action<TPiece, TStation?> setStation)
        {
            _getStation = getStation ?? throw new ArgumentNullException(nameof(getStation));
            _setStation = setStation ?? throw new ArgumentNullException(nameof(setStation));
        }

        public bool IsApplied => _piece != null;

        public bool IsAppliedTo(TPiece piece)
            => piece != null && ReferenceEquals(_piece, piece);

        public StationOverrideApplyResult Apply(TPiece piece)
        {
            if (piece == null)
                return StationOverrideApplyResult.InvalidPiece;

            var station = _getStation(piece);
            if (ReferenceEquals(_piece, piece))
            {
                if (station == null)
                    return StationOverrideApplyResult.AlreadyAbsent;
                if (!ReferenceEquals(station, _originalStation))
                    return StationOverrideApplyResult.Conflict;

                _setStation(piece, null);
                return StationOverrideApplyResult.Removed;
            }

            // A stationless runtime clone may have inherited the already-cleared value.
            // It must not displace the live prefab whose original station we still own.
            if (station == null)
                return StationOverrideApplyResult.AlreadyAbsent;

            if (_piece != null)
                Restore();

            _piece = piece;
            _originalStation = station;
            // Preserve the captured state if the setter throws: it may have mutated
            // the piece before failing, so later cleanup must still be able to restore it.
            _setStation(piece, null);
            return StationOverrideApplyResult.Removed;
        }

        public StationOverrideRestoreResult Restore()
        {
            var piece = _piece;
            var originalStation = _originalStation;
            if (piece == null)
                return StationOverrideRestoreResult.NothingToRestore;

            var currentStation = _getStation(piece);
            if (currentStation != null)
            {
                var result = ReferenceEquals(currentStation, originalStation)
                    ? StationOverrideRestoreResult.AlreadyRestored
                    : StationOverrideRestoreResult.Conflict;
                Forget();
                return result;
            }

            if (originalStation == null)
            {
                Forget();
                return StationOverrideRestoreResult.Conflict;
            }

            // Clear ownership only after the setter returns successfully. If it throws,
            // retain the exact captured state so cleanup can be retried safely.
            _setStation(piece, originalStation);
            Forget();
            return StationOverrideRestoreResult.Restored;
        }

        public void Forget()
        {
            _piece = null;
            _originalStation = null;
        }
    }
}
