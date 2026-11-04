import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { RecordMovementComponent } from './record-movement/record-movement.component';
import { TimelineComponent } from './timeline/timeline.component';
import { TrackingRoutingModule } from './tracking-routing.module';
import { TrackingComponent } from './tracking.component';

@NgModule({
  declarations: [TrackingComponent, TimelineComponent, RecordMovementComponent],
  imports: [SharedModule, TrackingRoutingModule],
})
export class TrackingModule {}
