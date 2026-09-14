using System;

namespace Treadwell.Core
{
    public sealed class PavedRoadStationOverride<TPiece, TStation>
        where TPiece : class
        where TStation : class
    {
        public const string VanillaPrefabName = "paved_road";
        public const string VanillaDisplayName = "$piece_pavedroad";
        public const string VanillaStationPrefabName = "piece_stonecutter";
        public const string VanillaStationDisplayName = "$piece_stonecutter";

        private readonly Func<TPiece, string> _piecePrefabName;
        private readonly Func<TPiece, string> _pieceDisplayName;
        private readonly Func<TPiece, TStation?> _getStation;
        private readonly Action<TPiece, TStation?> _setStation;
        private readonly Func<TStation, string> _stationPrefabName;
        private readonly Func<TStation, string> _stationDisplayName;
        private TPiece? _piece;
        private TStation? _originalStation;

        public PavedRoadStationOverride(
            Func<TPiece, string> piecePrefabName,
            Func<TPiece, string> pieceDisplayName,
            Func<TPiece, TStation?> getStation,
            Action<TPiece, TStation?> setStation,
            Func<TStation, string> stationPrefabName,
            Func<TStation, string> stationDisplayName)
        {
            _piecePrefabName = piecePrefabName ?? throw new ArgumentNullException(nameof(piecePrefabName));
            _pieceDisplayName = pieceDisplayName ?? throw new ArgumentNullException(nameof(pieceDisplayName));
            _getStation = getStation ?? throw new ArgumentNullException(nameof(getStation));
            _setStation = setStation ?? throw new ArgumentNullException(nameof(setStation));
            _stationPrefabName = stationPrefabName ?? throw new ArgumentNullException(nameof(stationPrefabName));
            _stationDisplayName = stationDisplayName ?? throw new ArgumentNullException(nameof(stationDisplayName));
        }

        public bool IsApplied => _piece != null;

        public bool IsAppliedTo(TPiece piece)
            => piece != null && ReferenceEquals(_piece, piece);

        public bool SetEnabled(bool enabled, TPiece? candidate)
        {
            if (!enabled)
                return Restore();
            return candidate != null && Apply(candidate);
        }

        public bool Apply(TPiece piece)
        {
            if (piece == null || !IsExactPavedRoad(piece))
                return false;

            if (ReferenceEquals(_piece, piece))
                return _getStation(piece) == null;

            if (_piece != null && !Restore())
                return false;

            var station = _getStation(piece);
            if (station == null || !IsExactStonecutter(station))
                return false;

            _piece = piece;
            _originalStation = station;
            try
            {
                _setStation(piece, null);
                return true;
            }
            catch
            {
                Forget();
                throw;
            }
        }

        public bool Restore()
        {
            var piece = _piece;
            var originalStation = _originalStation;
            if (piece == null)
                return true;

            if (_getStation(piece) != null)
            {
                Forget();
                return false;
            }

            if (originalStation == null)
            {
                Forget();
                return false;
            }

            try
            {
                _setStation(piece, originalStation);
                Forget();
                return true;
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

        private bool IsExactPavedRoad(TPiece piece)
            => string.Equals(_piecePrefabName(piece), VanillaPrefabName, StringComparison.Ordinal) &&
               string.Equals(_pieceDisplayName(piece), VanillaDisplayName, StringComparison.Ordinal);

        private bool IsExactStonecutter(TStation station)
            => string.Equals(_stationPrefabName(station), VanillaStationPrefabName, StringComparison.Ordinal) &&
               string.Equals(_stationDisplayName(station), VanillaStationDisplayName, StringComparison.Ordinal);
    }
}
