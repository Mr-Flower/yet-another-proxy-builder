using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MTGProxyBuilder.Core.Models;
using MTGProxyBuilder.Core.Services;

namespace MTGProxyBuilder.UI.Controls
{
    /// <summary>
    /// Card manipulation operations for the canvas: duplicate, delete, flip.
    /// Extracted from GridEditorCanvas to isolate business logic from UI.
    /// </summary>
    public static class CanvasOperations
    {
        public static void DuplicateCards(ObservableCollection<CardModel> cards, List<int> cardIndices, UndoService? undo)
        {
            undo?.SaveState(cards);

            foreach (var idx in cardIndices.OrderByDescending(i => i))
            {
                if (idx < 0 || idx >= cards.Count) continue;
                var original = cards[idx];
                var copy = new CardModel
                {
                    Name = original.Name,
                    ArtworkPath = original.ArtworkPath,
                    BackArtworkPath = original.BackArtworkPath,
                    OriginalBackArtworkPath = original.OriginalBackArtworkPath,
                    OverlayText = original.OverlayText,
                    ScryfallId = original.ScryfallId,
                    Quantity = original.Quantity,
                    IncludeBack = original.IncludeBack,
                    ManaCost = original.ManaCost,
                    CMC = original.CMC,
                    TypeLine = original.TypeLine,
                    OracleText = original.OracleText,
                    Rarity = original.Rarity,
                    Colors = original.Colors,
                    ColorIdentity = original.ColorIdentity,
                    SetCode = original.SetCode,
                    SetName = original.SetName,
                    CollectorNumber = original.CollectorNumber,
                    Artist = original.Artist,
                    Power = original.Power,
                    Toughness = original.Toughness,
                    Loyalty = original.Loyalty,
                    Keywords = original.Keywords,
                    DateAdded = DateTime.Now
                };
                cards.Insert(idx + 1, copy);
            }
        }

        public static void DeleteCards(ObservableCollection<CardModel> cards, List<int> cardIndices, UndoService? undo)
        {
            undo?.SaveState(cards);

            foreach (var idx in cardIndices.OrderByDescending(i => i))
            {
                if (idx >= 0 && idx < cards.Count)
                    cards.RemoveAt(idx);
            }
        }

        public static void FlipCards(HashSet<int> flippedIndices, List<int> cardIndices)
        {
            foreach (var idx in cardIndices)
            {
                if (!flippedIndices.Remove(idx))
                    flippedIndices.Add(idx);
            }
        }

        public static void FlipAll(ref bool allFlipped, HashSet<int> flippedIndices)
        {
            allFlipped = !allFlipped;
            flippedIndices.Clear();
        }
    }
}
