import re

with open('src/Daynote.Desktop/Views/MainWindow.axaml', 'r', encoding='utf-8') as f:
    text = f.read()

replacement = '''<ItemsControl.DataTemplates>
                          <DataTemplate DataType="pvm:TimelineNoteRow">
                            <Border Classes="panel" Padding="20,18">
                              <StackPanel>
                                <Button Classes="link" Command="{Binding OpenCommand}">
                                  <StackPanel Orientation="Horizontal" Spacing="7">
                                    <TextBlock FontSize="16" FontWeight="Bold" LetterSpacing="-0.3" Text="{Binding Title}" />
                                    <Path Data="{StaticResource Daynote.Desk.Geo.Star}" Fill="{DynamicResource Daynote.Desk.Brush.Sun}"
                                          VerticalAlignment="Center" IsVisible="{Binding IsFavorite}" />
                                  </StackPanel>
                                </Button>
                                <TextBlock Classes="muted" FontSize="13.5" LineHeight="24.3" TextWrapping="Wrap" Margin="0,8,0,0"
                                           Text="{Binding DisplayBody}" />
                                <Button Classes="link sun" FontSize="12.5" FontWeight="SemiBold" Margin="0,8,0,0"
                                        Content="{Binding ExpandLabel}" Command="{Binding ToggleExpandCommand}" IsVisible="{Binding IsTruncated}" />
                              </StackPanel>
                            </Border>
                          </DataTemplate>
                          <DataTemplate DataType="pvm:TimelineTodoRow">
                            <Border Classes="panel" Padding="20,12">
                              <StackPanel Orientation="Horizontal" Spacing="10">
                                <CheckBox IsChecked="{Binding IsDone}" IsHitTestVisible="False" VerticalAlignment="Center" />
                                <TextBlock FontSize="15" VerticalAlignment="Center" Text="{Binding Title}" />
                              </StackPanel>
                            </Border>
                          </DataTemplate>
                        </ItemsControl.DataTemplates>'''

text = re.sub(r'<ItemsControl\.ItemTemplate>\s*<DataTemplate x:DataType="pvm:TimelineNoteRow">.*?</DataTemplate>\s*</ItemsControl\.ItemTemplate>', replacement, text, flags=re.DOTALL)

with open('src/Daynote.Desktop/Views/MainWindow.axaml', 'w', encoding='utf-8') as f:
    f.write(text)
