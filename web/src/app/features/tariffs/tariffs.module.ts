import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { TariffDialogComponent } from './tariff-dialog/tariff-dialog.component';
import { TariffsRoutingModule } from './tariffs-routing.module';
import { TariffsComponent } from './tariffs.component';

@NgModule({
  declarations: [TariffsComponent, TariffDialogComponent],
  imports: [SharedModule, TariffsRoutingModule],
})
export class TariffsModule {}
