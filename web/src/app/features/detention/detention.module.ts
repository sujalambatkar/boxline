import { NgModule } from '@angular/core';

import { SharedModule } from '../../shared/shared.module';
import { DetentionResultComponent } from './detention-result/detention-result.component';
import { DetentionRoutingModule } from './detention-routing.module';
import { DetentionComponent } from './detention.component';

@NgModule({
  declarations: [DetentionComponent, DetentionResultComponent],
  imports: [SharedModule, DetentionRoutingModule],
})
export class DetentionModule {}
