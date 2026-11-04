import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { VoyageFormComponent } from './voyage-form/voyage-form.component';
import { VoyagesRoutingModule } from './voyages-routing.module';
import { VoyagesComponent } from './voyages.component';

@NgModule({
  declarations: [VoyagesComponent, VoyageFormComponent],
  imports: [SharedModule, VoyagesRoutingModule],
})
export class VoyagesModule {}
