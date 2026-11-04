import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { VesselsComponent } from './vessels.component';

const routes: Routes = [{ path: '', component: VesselsComponent }];

@NgModule({
  imports: [RouterModule.forChild(routes)],
  exports: [RouterModule]
})
export class VesselsRoutingModule { }
