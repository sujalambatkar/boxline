import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { VesselDialogComponent } from './vessel-dialog/vessel-dialog.component';
import { VesselsRoutingModule } from './vessels-routing.module';
import { VesselsComponent } from './vessels.component';

@NgModule({
  declarations: [VesselsComponent, VesselDialogComponent],
  imports: [SharedModule, VesselsRoutingModule],
})
export class VesselsModule {}
