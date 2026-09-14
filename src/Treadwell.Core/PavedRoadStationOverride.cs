using System;

namespace Treadwell.Core
{
    public enum StationOverrideApplyResult
    {
        NotExactPavedRoad,
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
        public const string VanillaPrefabName = "paved_road";
        public const string VanillaDisplayName = "$piece_pavedroad";
        public const string RuntimeCloneSuffix = "(Clone)";

        private readonly Func<TPiece, string?> _piecePrefabName;
        private readonly Func<TPiece, string?> _pieceDisplayName;
        private readonly Func<TPiece, TStation?> _getStation;
        private readonly Action<TPiece, TStation?> _setStation;
        private TPiece? _piece;
        private TStation? _originalStation;

        public PavedRoadStationOverride(
            Func<TPiece, string?> piecePrefabName,
            Func<TPiece, string?> pieceDisplayName,
            Func<TPiece, TStation?> getStation,
            Action<TPiece, TStation?> setStation)
        {
            _piecePrefabName = piecePrefabName ?? throw new ArgumentNullException(nameof(piecePrefabName));
            _pieceDisplayName = pieceDisplayName ?? throw new ArgumentNullException(nameof(pieceDisplayName));
            _getStation = getStation ?? throw new ArgumentNullException(nameof(getStation));
            _setStation = setStation ?? throw new ArgumentNullException(nameof(setStation));
        }

        public bool IsApplied => _piece != null;

        public bool IsAppliedTo(TPiece piece)
            => piece != null && ReferenceEquals(_piece, piece);

        public StationOverrideApplyResult Apply(TPiece piece)
        {
            if (piece == null || !IsExactPavedRoad(piece))
                return StationOverrideApplyResult.NotExactPavedRoad;

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
            try
            {
                _setStation(piece, null);
                return StationOverrideApplyResult.Removed;
            }
            catch
            {
                Forget();
                throw;
            }
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

            try
            {
                _setStation(piece, originalStation);
                Forget();
                return StationOverrideRestoreResult.Restored;
            }
            catch
            {
                Forget();
                throw;
            }
        }

        public void Forget()
        {
            _piece = null;
            _originalStation = null;
        }

        public bool IsExactPavedRoad(TPiece piece)
            => piece != null &&
               string.Equals(NormalizePrefabName(_piecePrefabName(piece)), VanillaPrefabName, StringComparison.Ordinal) &&
               string.Equals(_pieceDisplayName(piece), VanillaDisplayName, StringComparison.Ordinal);

        public static string? NormalizePrefabName(string? name)
        {
            if (name != null && name.EndsWith(RuntimeCloneSuffix, StringComparison.Ordinal))
                return name.Substring(0, name.Length - RuntimeCloneSuffix.Length);
            return name;
        }
    }
}
