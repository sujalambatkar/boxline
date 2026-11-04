import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';

import { DetentionComponent } from './detention.component';

const routes: Routes = [{ path: '', component: DetentionComponent }];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule],
})
export class DetentionRoutingModule {}
