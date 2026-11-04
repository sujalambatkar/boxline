import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { ContainerDialogComponent } from './container-dialog/container-dialog.component';
import { ContainersRoutingModule } from './containers-routing.module';
import { ContainersComponent } from './containers.component';

@NgModule({
  declarations: [ContainersComponent, ContainerDialogComponent],
  imports: [SharedModule, ContainersRoutingModule],
})
export class ContainersModule {}
