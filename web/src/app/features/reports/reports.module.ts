import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { ReportsRoutingModule } from './reports-routing.module';
import { ReportsComponent } from './reports.component';
import { UtilisationChartComponent } from './utilisation-chart/utilisation-chart.component';

@NgModule({
  declarations: [ReportsComponent, UtilisationChartComponent],
  imports: [SharedModule, ReportsRoutingModule],
})
export class ReportsModule {}
