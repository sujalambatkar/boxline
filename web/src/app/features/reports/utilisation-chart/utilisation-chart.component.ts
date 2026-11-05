import { Component, Input } from '@angular/core';

import { MonthlyUtilisation } from '../../../core/models/report.models';

export interface Bar {
  x: number;
  y: number;
  width: number;
  height: number;
  percent: number;
  /** "Nov 26" */
  label: string;
  showLabel: boolean;
  /** Tooltip and screen-reader text. */
  title: string;
}

export interface Tick {
  y: number;
  label: string;
}

/** The smallest "round" axis maximum (in percent) that fits the data; percentages never exceed 100. */
export function niceMax(maxPercent: number): number {
  return [5, 10, 20, 25, 40, 50, 60, 80, 100].find(candidate => candidate >= maxPercent) ?? 100;
}

/**
 * Bars of fleet utilisation per month, drawn as plain SVG (no chart library). The geometry is computed here, so it can be tested
 * without rendering; the template only places the shapes.
 */
@Component({
  selector: 'app-utilisation-chart',
  templateUrl: './utilisation-chart.component.html',
  styleUrls: ['./utilisation-chart.component.scss'],
})
export class UtilisationChartComponent {
  readonly width = 760;
  readonly height = 300;
  readonly margin = { top: 16, right: 16, bottom: 48, left: 52 };

  bars: Bar[] = [];
  ticks: Tick[] = [];
  axisMax = 10;
  peak: MonthlyUtilisation | null = null;
  peakLabel = '';

  private months: MonthlyUtilisation[] = [];

  @Input() set monthly(value: MonthlyUtilisation[] | null) {
    this.months = value ?? [];
    this.build();
  }

  get data(): MonthlyUtilisation[] {
    return this.months;
  }

  get plotWidth(): number {
    return this.width - this.margin.left - this.margin.right;
  }

  get plotHeight(): number {
    return this.height - this.margin.top - this.margin.bottom;
  }

  get summary(): string {
    return this.peak
      ? `Fleet utilisation by month, ${this.months.length} months. Highest: ${UtilisationChartComponent.monthLabel(this.peak.month, true)} at ${this.peak.utilisationPercent}% of the fleet.`
      : 'No movements in this period.';
  }

  trackByMonth(_: number, bar: Bar): string {
    return bar.label + bar.x;
  }

  private build(): void {
    const months = this.months;
    if (months.length === 0) {
      this.bars = [];
      this.ticks = [];
      this.peak = null;
      this.peakLabel = '';
      this.axisMax = 10;
      return;
    }

    const highest = Math.max(...months.map(m => m.utilisationPercent));
    this.peak = months.reduce((best, m) => (m.utilisationPercent > best.utilisationPercent ? m : best), months[0]);
    this.peakLabel = UtilisationChartComponent.monthLabel(this.peak.month);
    this.axisMax = niceMax(highest);

    const slot = this.plotWidth / months.length;
    const barWidth = Math.min(slot * 0.7, 44);
    const everyNth = Math.max(1, Math.ceil(months.length / 12));
    const bottom = this.margin.top + this.plotHeight;

    this.bars = months.map((m, i) => {
      const height = (m.utilisationPercent / this.axisMax) * this.plotHeight;
      return {
        x: this.margin.left + i * slot + (slot - barWidth) / 2,
        y: bottom - height,
        width: barWidth,
        height,
        percent: m.utilisationPercent,
        label: UtilisationChartComponent.monthLabel(m.month),
        showLabel: i % everyNth === 0,
        title:
          `${UtilisationChartComponent.monthLabel(m.month, true)}: ${m.utilisationPercent}% of the fleet ` +
          `(${m.activeContainers.toLocaleString('en-US')} containers), ${m.movements.toLocaleString('en-US')} movements`,
      };
    });

    this.ticks = [0, 0.25, 0.5, 0.75, 1].map(fraction => ({
      y: bottom - fraction * this.plotHeight,
      label: `${+(fraction * this.axisMax).toFixed(1)}%`,
    }));
  }

  /** "Nov 26" (short) or "Nov 2026" (long), in UTC so a month never slips to its neighbour. */
  static monthLabel(isoDate: string, long = false): string {
    return new Date(isoDate).toLocaleDateString('en-GB', { month: 'short', year: long ? 'numeric' : '2-digit', timeZone: 'UTC' });
  }
}
