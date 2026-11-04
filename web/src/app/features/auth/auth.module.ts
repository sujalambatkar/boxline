import { NgModule } from '@angular/core';
import { MatCardModule } from '@angular/material/card';

import { SharedModule } from '../../shared/shared.module';
import { AuthRoutingModule } from './auth-routing.module';
import { LoginComponent } from './login.component';

@NgModule({
  declarations: [LoginComponent],
  imports: [SharedModule, MatCardModule, AuthRoutingModule],
})
export class AuthModule {}
