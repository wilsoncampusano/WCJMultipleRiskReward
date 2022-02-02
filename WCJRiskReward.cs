#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using NinjaTrader.Core.FloatingPoint;
using NinjaTrader.Gui;
using NinjaTrader.Gui.Chart;
using NinjaTrader.Gui.Tools;

#endregion

//This namespace holds Drawing tools in this folder and is required. Do not change it. 
namespace NinjaTrader.NinjaScript.DrawingTools
{
		public class WCJRiskRewardR : FibonacciRetracements 
	{
		Point anchorExtensionPoint;

		[Display(Order = 3)]
		public ChartAnchor ExtensionAnchor { get; set; }
		
		public override IEnumerable<ChartAnchor> Anchors { get { return new[] { StartAnchor, EndAnchor, ExtensionAnchor }; } }
		
		[Display(ResourceType = typeof(Custom.Resource), Name = "NinjaScriptDrawingToolRulerYValueDisplayUnit", GroupName = "NinjaScriptGeneral", Order = 3)]
		public ValueUnit				DisplayUnit 		{ get; set; }
		
		protected new Tuple<Point, Point> GetPriceLevelLinePoints(PriceLevel priceLevel, ChartControl chartControl, ChartScale chartScale, bool isInverted)
		{
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 		= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double totalPriceRange		= EndAnchor.Price - StartAnchor.Price;
			// dont forget user could start/end draw backwards
			double anchorMinX 		= Math.Min(anchorStartPoint.X, anchorEndPoint.X);
			double anchorMaxX 		= Math.Max(anchorStartPoint.X, anchorEndPoint.X);
			double lineStartX		= IsExtendedLinesLeft ? chartPanel.X : anchorMinX;
			double lineEndX 		= IsExtendedLinesRight ? chartPanel.X + chartPanel.W : anchorMaxX;
			double levelY			= priceLevel.GetY(chartScale, ExtensionAnchor.Price, totalPriceRange, isInverted);
			return new Tuple<Point, Point>(new Point(lineStartX, levelY), new Point(lineEndX, levelY));
		}
		
		private new void DrawPriceLevelText(ChartPanel chartPanel, ChartScale chartScale, double minX, double maxX, double y, double price, PriceLevel priceLevel)
		{
			if (TextLocation == TextLocation.Off || priceLevel == null || priceLevel.Stroke == null || priceLevel.Stroke.BrushDX == null)
				return;

			// make a rectangle that sits right at our line, depending on text alignment settings
			SimpleFont wpfFont = chartPanel.ChartControl.Properties.LabelFont ?? new SimpleFont();
			SharpDX.DirectWrite.TextFormat textFormat = wpfFont.ToDirectWriteTextFormat();
			textFormat.TextAlignment = SharpDX.DirectWrite.TextAlignment.Leading;
			textFormat.WordWrapping = SharpDX.DirectWrite.WordWrapping.NoWrap;

			string str = GetPriceString(price, priceLevel, chartPanel);

			// when using extreme alignments, give a few pixels of padding on the text so we dont end up right on the edge
			const double edgePadding = 2f;
			float layoutWidth = (float)Math.Abs(maxX - minX); // always give entire available width for layout
			// dont use max x for max text width here, that can break inside left/right when extended lines are on
			SharpDX.DirectWrite.TextLayout textLayout = new SharpDX.DirectWrite.TextLayout(Core.Globals.DirectWriteFactory, str, textFormat, layoutWidth, textFormat.FontSize);

			double drawAtX = minX;

			if (IsExtendedLinesLeft && TextLocation == TextLocation.ExtremeLeft)
				drawAtX = chartPanel.X + edgePadding;
			else if (IsExtendedLinesRight && TextLocation == TextLocation.ExtremeRight)
				drawAtX = chartPanel.X + chartPanel.W - textLayout.Metrics.Width;
			else
			{
				if (TextLocation == TextLocation.InsideLeft || TextLocation == TextLocation.ExtremeLeft)
					drawAtX = minX <= maxX ? minX - 1 : maxX - 1;
				else
					drawAtX = minX > maxX ? minX - textLayout.Metrics.Width : maxX - textLayout.Metrics.Width;
			}

			// we also move our y value up by text height so we draw label above line like NT7.
			RenderTarget.DrawTextLayout(new SharpDX.Vector2((float)drawAtX, (float)(y - textFormat.FontSize - edgePadding)), textLayout, priceLevel.Stroke.BrushDX, SharpDX.Direct2D1.DrawTextOptions.NoSnap);

			textFormat.Dispose();
			textLayout.Dispose();
		}

		public override Cursor GetCursor(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, Point point)
		{
			if (DrawingState != DrawingState.Normal)
				return base.GetCursor(chartControl, chartPanel, chartScale, point);

			// draw move cursor if cursor is near line path anywhere
			Point startAnchorPixelPoint	= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);

			ChartAnchor closest = GetClosestAnchor(chartControl, chartPanel, chartScale, CursorSensitivity, point);
			if (closest != null)
			{
				// show arrow until they try to move it
				if (IsLocked)
					return Cursors.Arrow;
				return closest == StartAnchor ? Cursors.SizeNESW : Cursors.SizeNWSE;
			}

			// for extensions, we want to see if the cursor along the following lines (represented as vectors):
			// start -> end, end -> ext, ext start -> ext end
			Point	endAnchorPixelPoint			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point	extPixelPoint				= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Tuple<Point, Point> extYLinePoints	= GetTranslatedExtensionYLine(chartControl, chartScale);

			Vector startEndVec	= endAnchorPixelPoint - startAnchorPixelPoint;
			Vector endExtVec	= extPixelPoint - endAnchorPixelPoint;
			Vector extYVec		= extYLinePoints.Item2 - extYLinePoints.Item1;
			// need to have an actual point to run vector along, so glue em together here
			if (new[] {	new Tuple<Vector, Point>(startEndVec, startAnchorPixelPoint), 
						new Tuple<Vector, Point>(endExtVec, endAnchorPixelPoint), 
						new Tuple<Vector, Point>(extYVec, extYLinePoints.Item1)}
					.Any(chkTup => MathHelper.IsPointAlongVector(point, chkTup.Item2, chkTup.Item1, CursorSensitivity)))
				return IsLocked ? Cursors.Arrow : Cursors.SizeAll;
			return null;
		}

		private Point GetEndLineMidpoint(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point endPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point startPoint 		= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			return new Point((endPoint.X + startPoint.X) / 2, (endPoint.Y + startPoint.Y) / 2);
		}

		public sealed override Point[] GetSelectionPoints(ChartControl chartControl, ChartScale chartScale)
		{
			Point[] pts = base.GetSelectionPoints(chartControl, chartScale);
			if (!ExtensionAnchor.IsEditing || !EndAnchor.IsEditing)
			{
				// match NT7, show 3 points along ext based on actually drawn line
				Tuple<Point, Point> extYLine = GetTranslatedExtensionYLine(chartControl, chartScale);	
				Point midExtYPoint = extYLine.Item1 + (extYLine.Item2 - extYLine.Item1) / 2;
				Point midEndPoint = GetEndLineMidpoint(chartControl, chartScale);
				return pts.Union(new[]{extYLine.Item1, extYLine.Item2, midExtYPoint, midEndPoint}).ToArray();
			}
			return pts;
		}

		private string GetPriceString(double price, PriceLevel priceLevel, ChartPanel chartPanel)
		{
			// note, dont use MasterInstrument.FormatPrice() as it will round value to ticksize which we do not want
			string priceStr = price.ToString(Core.Globals.GetTickFormatString(AttachedTo.Instrument.MasterInstrument.TickSize));
			double pct = priceLevel.Value / 100;
			
			string priceString;
			double yValueEntry	= AttachedTo.Instrument.MasterInstrument.RoundToTickSize(StartAnchor.Price);
			double yValueEnd = AttachedTo.Instrument.MasterInstrument.RoundToTickSize(EndAnchor.Price);
			double tickSize		= AttachedTo.Instrument.MasterInstrument.TickSize;
			double pointValue	= AttachedTo.Instrument.MasterInstrument.PointValue;
			var priceAtLevel = AttachedTo.Instrument.MasterInstrument.FormatPrice(price );
	
			switch (DisplayUnit)
			{
				case ValueUnit.Currency:
					priceString = StartAnchor.Price > EndAnchor.Price ?
							Core.Globals.FormatCurrency(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(StartAnchor.Price - EndAnchor.Price) / tickSize * (tickSize * pointValue) * pct):
					Core.Globals.FormatCurrency(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(EndAnchor.Price - StartAnchor.Price) / tickSize * (tickSize * pointValue) * pct);
					break;
				case ValueUnit.Ticks:
					priceString = 
						(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(pct*Math.Abs((StartAnchor.Price - EndAnchor.Price) / tickSize))).ToString("F0")+ " "+ValueUnit.Ticks.ToString();
					break;
				case ValueUnit.Pips:
					priceString = 
						(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(pct*Math.Abs(StartAnchor.Price - EndAnchor.Price))).ToString("F0") + " "+ValueUnit.Pips.ToString();
					break;
				default:
					priceString = StartAnchor.Price > EndAnchor.Price ?
							Core.Globals.FormatCurrency(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(StartAnchor.Price - EndAnchor.Price) / tickSize * (tickSize * pointValue) * pct):
					Core.Globals.FormatCurrency(AttachedTo.Instrument.MasterInstrument.RoundToTickSize(EndAnchor.Price - StartAnchor.Price) / tickSize * (tickSize * pointValue) * pct);
					break;
			}
			
			var flatFormat = "{0} {1} @ {2}";
			var mainFormat = "{0} {1}:{2} {3} @ {4}";
			string str = "";
			
			var levelType = "Stop Loss";
			if(pct < 0)
			{
				str = string.Format(flatFormat, 
				levelType ,priceString, priceAtLevel);
			}
			else if(pct == 0)
			{
				levelType = "Entry Point";
				str = string.Format(flatFormat, 
				levelType ,priceString, priceAtLevel);
			}
			else if(pct >= 0.1)
			{
				levelType = "Target";
				str = string.Format(mainFormat, 
				levelType, pct,"1" ,priceString,priceAtLevel );
			}
			
	
			return str;
		}

		private Tuple<Point, Point> GetTranslatedExtensionYLine(ChartControl chartControl, ChartScale chartScale)
		{
			ChartPanel chartPanel	= chartControl.ChartPanels[PanelIndex];
			Point extPoint 			= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point startPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			double minLevelY		= double.MaxValue;
			foreach (Tuple<Point, Point> tup in PriceLevels.Where(pl => pl.IsVisible).Select(pl => GetPriceLevelLinePoints(pl, chartControl, chartScale, false)))
			{
				Vector vecToExtension	= extPoint - startPoint;
				Point adjStartPoint		= new Point((tup.Item1 + vecToExtension).X, tup.Item1.Y);

				minLevelY = Math.Min(adjStartPoint.Y, minLevelY);
			}
			if (minLevelY.ApproxCompare(double.MaxValue) == 0 )
				return new Tuple<Point, Point>(new Point(extPoint.X, extPoint.Y), new Point(extPoint.X, extPoint.Y));
			return new Tuple<Point, Point>(new Point(extPoint.X, minLevelY), new Point(extPoint.X, anchorExtensionPoint.Y));
		}

		public override object Icon { get { return Icons.DrawRiskReward; } }

		public override bool IsAlertConditionTrue(AlertConditionItem conditionItem, Condition condition, ChartAlertValue[] values, ChartControl chartControl, ChartScale chartScale)
		{
			PriceLevel priceLevel = conditionItem.Tag as PriceLevel;
			if (priceLevel == null)
				return false;
			ChartPanel chartPanel		= chartControl.ChartPanels[PanelIndex];
			Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
			Point anchorStartPoint 		= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point extensionPoint	 	= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			// note these points X will be based on start/end, so move to our extension 
			Vector vecToExtension		= extensionPoint - anchorStartPoint;
			Point adjStartPoint			= plp.Item1 + vecToExtension;
			Point adjEndPoint			= plp.Item2 + vecToExtension;
			return CheckAlertRetracementLine(condition, adjStartPoint, adjEndPoint, chartControl, chartScale, values);
		}

		public override void OnMouseDown(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			// because we have a third anchor we need to do some extra stuff here
			
			switch (DrawingState)
			{
				case DrawingState.Building:
					if (StartAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(StartAnchor);
						// give end anchor something to start with so we dont try to render it with bad values right away
						dataPoint.CopyDataValues(EndAnchor);
						StartAnchor.IsEditing = false;
					}
					else if (EndAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(EndAnchor);
						EndAnchor.IsEditing = false;

						// give extension anchor something nearby to start with
						dataPoint.CopyDataValues(ExtensionAnchor);
					}
					else if (ExtensionAnchor.IsEditing)
					{
						dataPoint.CopyDataValues(ExtensionAnchor);
						ExtensionAnchor.IsEditing = false;
					}
					
					// is initial building done (all anchors set)
					if (Anchors.All(a => !a.IsEditing))
					{
						DrawingState 	= DrawingState.Normal;
						IsSelected 		= false; 
					}
					break;
				case DrawingState.Normal:
					Point point = dataPoint.GetPoint(chartControl, chartPanel, chartScale);
					// first try base mouse down
					base.OnMouseDown(chartControl, chartPanel, chartScale, dataPoint);
					if (DrawingState != DrawingState.Normal)
						break;
					// now check if they clicked along extension fibs Y line and correctly select if so
					Tuple<Point, Point> extYLinePoints	= GetTranslatedExtensionYLine(chartControl, chartScale);
					Vector extYVec						= extYLinePoints.Item2 - extYLinePoints.Item1;
					Point pointDeviceY = new Point(point.X, ConvertToVerticalPixels(chartControl, chartPanel, point.Y));
					// need to have an actual point to run vector along, so glue em together here
					if (MathHelper.IsPointAlongVector(pointDeviceY, extYLinePoints.Item1, extYVec, CursorSensitivity))
						DrawingState = DrawingState.Moving;
					else
						IsSelected = false;

					break;
			}
		}
		
		public override void OnMouseMove(ChartControl chartControl, ChartPanel chartPanel, ChartScale chartScale, ChartAnchor dataPoint)
		{
			if (IsLocked && DrawingState != DrawingState.Building)
				return;
			
			base.OnMouseMove(chartControl, chartPanel, chartScale, dataPoint);
			
			if (DrawingState == DrawingState.Building && ExtensionAnchor.IsEditing)
				dataPoint.CopyDataValues(ExtensionAnchor);
		}
		
		protected override void OnStateChange()
		{
			if (State == State.SetDefaults)
			{
				AnchorLineStroke 			= new Stroke(Brushes.DarkGray, DashStyleHelper.Solid, 1f, 50);
				Name 						= "WCJRiskRewardR";
				Description 				= "Creado por Wilson Campusano como alternativa a RiskReward." ;
				PriceLevelOpacity			= 5;
				StartAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				ExtensionAnchor				= new ChartAnchor { IsEditing = true, DrawingTool = this };
				EndAnchor					= new ChartAnchor { IsEditing = true, DrawingTool = this };
				StartAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorStart;
				EndAnchor.DisplayName		= Custom.Resource.NinjaScriptDrawingToolAnchorEnd;
				ExtensionAnchor.DisplayName	= Custom.Resource.NinjaScriptDrawingToolAnchorExtension;
			}
			else if (State == State.Configure)
			{
				if (PriceLevels.Count == 0)
				{
					PriceLevels.Add(new PriceLevel(-100,		Brushes.Red));
					PriceLevels.Add(new PriceLevel(0,		Brushes.Black));
					PriceLevels.Add(new PriceLevel(100,		Brushes.Green));
					PriceLevels.Add(new PriceLevel(200,		Brushes.DarkGray));
					PriceLevels.Add(new PriceLevel(300,		Brushes.DarkGray));
					
				}
			}
			else if (State == State.Terminated)
				Dispose();
		}
		
		public override void OnRender(ChartControl chartControl, ChartScale chartScale)
		{
			// nothing is drawn yet
			if (Anchors.All(a => a.IsEditing)) 
				return;
			
			RenderTarget.AntialiasMode = SharpDX.Direct2D1.AntialiasMode.PerPrimitive;
			// get x distance of the line, this will be basis for our levels
			// unless extend left/right is also on
			ChartPanel chartPanel			= chartControl.ChartPanels[PanelIndex];
			Point anchorStartPoint 			= StartAnchor.GetPoint(chartControl, chartPanel, chartScale);
			Point anchorEndPoint 			= EndAnchor.GetPoint(chartControl, chartPanel, chartScale);
			
			anchorExtensionPoint			= ExtensionAnchor.GetPoint(chartControl, chartPanel, chartScale);
			AnchorLineStroke.RenderTarget	= RenderTarget;
			
			// align to full pixel to avoid unneeded aliasing
			double strokePixAdj			= (AnchorLineStroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
			Vector pixelAdjustVec		= new Vector(strokePixAdj, strokePixAdj);

			SharpDX.Vector2 startVec	= (anchorStartPoint + pixelAdjustVec).ToVector2();
			SharpDX.Vector2 endVec		= (anchorEndPoint + pixelAdjustVec).ToVector2();
			RenderTarget.DrawLine(startVec, endVec, AnchorLineStroke.BrushDX, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
			
			// is second anchor set yet? check both so we correctly redraw during extension anchor editing
			if (ExtensionAnchor.IsEditing && EndAnchor.IsEditing)
				return;
			
			SharpDX.Vector2			extVector	= anchorExtensionPoint.ToVector2();
			SharpDX.Direct2D1.Brush	tmpBrush	= IsInHitTest ? chartControl.SelectionBrush : AnchorLineStroke.BrushDX;
			RenderTarget.DrawLine(endVec, extVector, tmpBrush, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
	
			if (PriceLevels == null || !PriceLevels.Any() || IsInHitTest)
				return;

			SetAllPriceLevelsRenderTarget();

			double minLevelY = float.MaxValue;
			double maxLevelY = float.MinValue;
			Point lastStartPoint = new Point(0, 0);
			Stroke lastStroke = null;

			int count = 0;
			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
				// note these points X will be based on start/end, so move to our extension
				Vector vecToExtension		= anchorExtensionPoint - anchorStartPoint;
				Point startTranslatedToExt	= plp.Item1 + vecToExtension;
				Point endTranslatedToExt	= plp.Item2 + vecToExtension;
				
				// dont nuke extended X if extend left/right is on
				double startX 				= IsExtendedLinesLeft ? plp.Item1.X : startTranslatedToExt.X;
				double endX 				= IsExtendedLinesRight ? plp.Item2.X : endTranslatedToExt.X;
				Point adjStartPoint			= new Point(startX, plp.Item1.Y);
				Point adjEndPoint			= new Point(endX, plp.Item2.Y);

				// align to full pixel to avoid unneeded aliasing
				double plPixAdjust			=	(priceLevel.Stroke.Width % 2.0).ApproxCompare(0) == 0 ? 0.5d : 0d;
				Vector plPixAdjustVec		= new Vector(plPixAdjust, plPixAdjust);
				
				// don't hit test on the price level line & text (match NT7 here), but do keep track of the min/max y
				if (!IsInHitTest)
				{
					Point startPoint = adjStartPoint + plPixAdjustVec;
					Point endPoint = adjEndPoint + plPixAdjustVec;
					
					RenderTarget.DrawLine(startPoint.ToVector2(), endPoint.ToVector2(), 
											priceLevel.Stroke.BrushDX, priceLevel.Stroke.Width, priceLevel.Stroke.StrokeStyle);

					if (lastStroke == null)
						lastStroke = new Stroke();
					else
					{
						SharpDX.RectangleF borderBox = new SharpDX.RectangleF((float)lastStartPoint.X, (float)lastStartPoint.Y,
							(float)(endPoint.X - lastStartPoint.X), (float)(endPoint.Y - lastStartPoint.Y));

						RenderTarget.FillRectangle(borderBox, lastStroke.BrushDX);
					}
					priceLevel.Stroke.CopyTo(lastStroke);
					lastStroke.Opacity = PriceLevelOpacity;
					lastStartPoint = startPoint;
				}
				minLevelY = Math.Min(adjStartPoint.Y, minLevelY);
				maxLevelY = Math.Max(adjStartPoint.Y, maxLevelY);
				count++;
			}

			foreach (PriceLevel priceLevel in PriceLevels.Where(pl => pl.IsVisible && pl.Stroke != null).OrderBy(pl => pl.Value))
			{
				if (!IsInHitTest)
				{
					Tuple<Point, Point>	plp		= GetPriceLevelLinePoints(priceLevel, chartControl, chartScale, false);
					// note these points X will be based on start/end, so move to our extension
					Vector vecToExtension		= anchorExtensionPoint - anchorStartPoint;
					Point startTranslatedToExt	= plp.Item1 + vecToExtension;
				
					// dont nuke extended X if extend left/right is on
					double startX 				= IsExtendedLinesLeft ? plp.Item1.X : startTranslatedToExt.X;
					Point adjStartPoint			= new Point(startX, plp.Item1.Y);

					double extMinX = anchorExtensionPoint.X;
					double extMaxX = anchorExtensionPoint.X + anchorEndPoint.X - anchorStartPoint.X; // actual width of lines before extension

					double totalPriceRange	= EndAnchor.Price - StartAnchor.Price;
					double price			= priceLevel.GetPrice(ExtensionAnchor.Price, totalPriceRange, false);
					DrawPriceLevelText(chartPanel, chartScale, extMinX, extMaxX, adjStartPoint.Y, price, priceLevel);
				}
			}

			// lastly draw the left edge line  at our fib lines line NT7. dont use lines start x here, it will be left edge when
			// extend left is on which we do not want
			if (count > 0)
				RenderTarget.DrawLine(new SharpDX.Vector2(extVector.X, (float)minLevelY), new SharpDX.Vector2(extVector.X, (float)maxLevelY), AnchorLineStroke.BrushDX, AnchorLineStroke.Width, AnchorLineStroke.StrokeStyle);
		}
	}

}
