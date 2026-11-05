import { ComponentFixture, TestBed } from '@angular/core/testing';

import { MonthlyUtilisation } from '../../../core/models/report.models';
import { niceMax, UtilisationChartComponent } from './utilisation-chart.component';

describe('niceMax', () => {
  it('rounds the highest value up to the next round axis maximum', () => {
    const cases: [number, number][] = [[0, 5], [4.9, 5], [5, 5], [5.1, 10], [10, 10], [18, 20], [21, 25], [33, 40], [45, 50], [55, 60], [61, 80], [99.9, 100], [100, 100]];
    for (const [value, expected] of cases) {
      expect(niceMax(value)).withContext(String(value)).toBe(expected);
    }
  });

  it('never goes beyond 100%', () => {
    expect(niceMax(140)).toBe(100);
  });
});

describe('UtilisationChartComponent', () => {
  let fixture: ComponentFixture<UtilisationChartComponent>;
  let component: UtilisationChartComponent;
  const el = () => fixture.nativeElement as HTMLElement;

  const month = (iso: string, percent: number, active = 100, movements = 1_000): MonthlyUtilisation => ({
    month: iso, movements, activeContainers: active, utilisationPercent: percent,
  });

  const show = (months: MonthlyUtilisation[] | null) => {
    component.monthly = months;
    fixture.detectChanges();
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({ declarations: [UtilisationChartComponent] }).compileComponents();
    fixture = TestBed.createComponent(UtilisationChartComponent);
    component = fixture.componentInstance;
  });

  it('says so when there is nothing to draw', () => {
    show([]);

    expect(el().textContent).toContain('No movements in this period.');
    expect(el().querySelector('svg')).toBeNull();
    expect(component.summary).toBe('No movements in this period.');
  });

  it('treats null like an empty list', () => {
    show(null);

    expect(component.bars).toEqual([]);
  });

  it('scales every bar to the axis maximum (height = percent / max × plot height)', () => {
    show([month('2026-09-01', 10), month('2026-10-01', 20), month('2026-11-01', 40)]);

    expect(component.axisMax).toBe(40);
    const plot = component.plotHeight;
    expect(component.bars.map(b => b.height)).toEqual([plot * 0.25, plot * 0.5, plot]);
    // Every bar stands on the same baseline.
    const baseline = component.margin.top + plot;
    component.bars.forEach(b => expect(b.y + b.height).toBeCloseTo(baseline, 6));
  });

  it('places the bars left to right in month order, evenly, and caps their width', () => {
    show([month('2026-09-01', 10), month('2026-10-01', 20), month('2026-11-01', 40)]);

    const xs = component.bars.map(b => b.x);
    expect(xs).toEqual([...xs].sort((a, b) => a - b));
    expect(new Set(component.bars.map(b => b.width)).size).toBe(1);
    expect(component.bars[0].width).toBeLessThanOrEqual(44);
    expect(xs[1] - xs[0]).toBeCloseTo(xs[2] - xs[1], 6);
  });

  it('draws a zero bar for a month with no utilisation, and does not crash when everything is zero', () => {
    show([month('2026-09-01', 0), month('2026-10-01', 0)]);

    expect(component.bars.every(b => b.height === 0)).toBeTrue();
    expect(component.axisMax).toBe(5);
  });

  it('labels months in UTC so a month never slips to its neighbour', () => {
    expect(UtilisationChartComponent.monthLabel('2026-03-01')).toBe('Mar 26');
    expect(UtilisationChartComponent.monthLabel('2026-01-01')).toBe('Jan 26');
    expect(UtilisationChartComponent.monthLabel('2026-12-01', true)).toBe('Dec 2026');
  });

  it('gives each bar a tooltip with the percentage, the containers and the movements', () => {
    show([month('2026-11-01', 54.3, 13_682, 41_200)]);

    expect(component.bars[0].title).toBe('Nov 2026: 54.3% of the fleet (13,682 containers), 41,200 movements');
    expect(el().querySelector('[data-testid="bar"] title')?.textContent).toBe(component.bars[0].title);
  });

  it('draws five gridlines from zero to the axis maximum', () => {
    show([month('2026-09-01', 10), month('2026-11-01', 40)]);

    expect(component.ticks.map(t => t.label)).toEqual(['0%', '10%', '20%', '30%', '40%']);
    expect(component.ticks[0].y).toBeCloseTo(component.margin.top + component.plotHeight, 6);
    expect(component.ticks[4].y).toBeCloseTo(component.margin.top, 6);
  });

  it('keeps every label for a short series, and thins them for a long one', () => {
    show(Array.from({ length: 6 }, (_, i) => month(`2026-0${i + 1}-01`, 10)));
    expect(component.bars.filter(b => b.showLabel)).toHaveSize(6);

    show(Array.from({ length: 25 }, (_, i) => month(new Date(Date.UTC(2024, 10 + i, 1)).toISOString().slice(0, 10), 10)));
    expect(component.bars).toHaveSize(25);
    expect(component.bars.filter(b => b.showLabel)).toHaveSize(9);
    expect(component.bars[0].showLabel).toBeTrue();
  });

  it('names the highest month in the caption and the accessible summary', () => {
    show([month('2026-09-01', 10), month('2026-10-01', 40.5), month('2026-11-01', 20)]);

    expect(component.peakLabel).toBe('Oct 26');
    expect(el().querySelector('.peak')?.textContent).toContain('highest 40.5% in Oct 26');
    expect(el().querySelector('svg')?.getAttribute('aria-label')).toContain('Highest: Oct 2026 at 40.5% of the fleet');
    expect(el().querySelector('svg')?.getAttribute('role')).toBe('img');
  });

  it('offers the same figures as a table for screen readers', () => {
    show([month('2026-09-01', 10, 50, 600), month('2026-10-01', 20, 90, 900)]);

    const rows = Array.from(el().querySelectorAll('table.sr-only tbody tr')).map(r => Array.from(r.querySelectorAll('td')).map(c => c.textContent));
    expect(rows).toEqual([['2026-09-01', '10%', '50', '600'], ['2026-10-01', '20%', '90', '900']]);
  });

  it('redraws when the data changes', () => {
    show([month('2026-09-01', 10)]);
    expect(el().querySelectorAll('[data-testid="bar"]')).toHaveSize(1);

    show([month('2026-09-01', 10), month('2026-10-01', 30)]);

    expect(el().querySelectorAll('[data-testid="bar"]')).toHaveSize(2);
  });
});
